using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Mcpserver.Application.Interfaces;
using Mcpserver.Domain.Contracts.Auth;
using Mcpserver.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Novell.Directory.Ldap;

namespace Mcpserver.Infrastructure.Services;

public sealed class AdAuthService : IAdAuthService
{
    private readonly AzureAdSettings _azure;
    private readonly ActiveDirectorySettings _ad;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AdAuthService> _logger;
    private readonly HttpClient _httpClient;

    public AdAuthService(
        IOptions<AzureAdSettings> azure,
        IOptions<ActiveDirectorySettings> ad,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AdAuthService> logger)
    {
        _azure = azure.Value;
        _ad = ad.Value;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
    }

    public async Task<AdAuthResult> AuthenticateAsync(CancellationToken ct)
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx is null)
            return Fail("HttpContext não disponível.");

        var objectId = ctx.Request.Headers["X-Ms-Client-Object-Id"].FirstOrDefault();
        var tenantIdHeader = ctx.Request.Headers["X-Ms-Client-Tenant-Id"].FirstOrDefault();
        var principalId = ctx.Request.Headers["X-Ms-Client-Principal-Id"].FirstOrDefault();
        var principalHeader = ctx.Request.Headers["X-MS-CLIENT-PRINCIPAL"].FirstOrDefault();

        _logger.LogInformation(
            "Headers de identidade recebidos. ObjectId={ObjectId} PrincipalId={PrincipalId} TenantIdHeader={TenantIdHeader} HasPrincipalHeader={HasPrincipalHeader}",
            objectId ?? "null",
            principalId ?? "null",
            tenantIdHeader ?? "null",
            !string.IsNullOrWhiteSpace(principalHeader));

        GraphUserInfo? graphUser = null;

        if (!string.IsNullOrWhiteSpace(objectId))
            graphUser = await GetGraphUserByIdAsync(objectId, ct);

        if (graphUser is null && !string.IsNullOrWhiteSpace(principalHeader))
        {
            var principalData = TryDecodeClientPrincipal(principalHeader);

            var upnFromClaims =
                principalData?.FindClaim("preferred_username") ??
                principalData?.FindClaim("upn") ??
                principalData?.FindClaim("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn") ??
                principalData?.FindClaim("email") ??
                principalData?.FindClaim("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress");

            var oidFromClaims =
                principalData?.FindClaim("oid") ??
                principalData?.FindClaim("http://schemas.microsoft.com/identity/claims/objectidentifier");

            _logger.LogInformation(
                "Claims extraídas do X-MS-CLIENT-PRINCIPAL. UPN={Upn} OID={Oid}",
                upnFromClaims ?? "null",
                oidFromClaims ?? "null");

            if (!string.IsNullOrWhiteSpace(oidFromClaims))
                graphUser = await GetGraphUserByIdAsync(oidFromClaims, ct);

            if (graphUser is null && !string.IsNullOrWhiteSpace(upnFromClaims))
                graphUser = await GetGraphUserByUpnAsync(upnFromClaims, ct);
        }

        if (graphUser is null)
            return Fail("Não foi possível identificar o usuário atual pelos headers recebidos.");

        var upn = graphUser.UserPrincipalName;
        if (string.IsNullOrWhiteSpace(upn))
            return Fail("Usuário identificado, mas sem userPrincipalName.");

        var samAccount = upn.Contains('@') ? upn.Split('@')[0] : upn;

        try
        {
            _logger.LogInformation(
                "Consultando usuário no AD. SamAccount={SamAccount} Upn={Upn} Host={Host} BaseDn={BaseDn}",
                samAccount, upn, _ad.Host, _ad.BaseDn);

            var user = await SearchUserInAdAsync(samAccount, upn, ct);

            if (user is null)
                return Fail($"Usuário '{upn}' identificado, mas não encontrado no AD.");

            if (!user.Enabled)
                return Fail($"Conta '{samAccount}' está desativada no Active Directory.");

            user.DisplayName ??= graphUser.DisplayName;
            user.Email ??= graphUser.Mail;
            user.Upn = upn;

            _logger.LogInformation(
                "Usuário autenticado com sucesso. Upn={Upn} Nome={DisplayName} Email={Email} Departamento={Department} Cargo={Title}",
                user.Upn,
                user.DisplayName,
                user.Email,
                user.Department,
                user.Title);

            return new AdAuthResult
            {
                Authenticated = true,
                User = user,
                Source = "Copilot Headers → Microsoft Graph → AD on-premises"
            };
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "Erro LDAP ao autenticar usuário. Upn={Upn}", upn);
            return Fail($"Erro LDAP: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao autenticar usuário. Upn={Upn}", upn);
            return Fail($"Erro inesperado: {ex.Message}");
        }
    }

    private async Task<AdUserDto?> SearchUserInAdAsync(string samAccount, string upn, CancellationToken ct)
    {
        using var conn = new Novell.Directory.Ldap.LdapConnection();

        await conn.ConnectAsync(_ad.Host, _ad.Port, ct);
        await conn.BindAsync(_ad.BindUser, _ad.BindPass, ct);

        var filter = $"(&(objectClass=user)(objectCategory=person)(sAMAccountName={Escape(samAccount)}))";
        var attrs = new[]
        {
        "sAMAccountName",
        "displayName",
        "mail",
        "department",
        "title",
        "memberOf",
        "userAccountControl"
    };

        var search = await conn.SearchAsync(
            _ad.BaseDn,
            Novell.Directory.Ldap.LdapConnection.ScopeSub,
            filter,
            attrs,
            false,
            ct
        );

        await foreach (var entry in search.WithCancellation(ct))
        {
            var attrSet = entry.GetAttributeSet();

            var username = attrSet.GetAttribute("sAMAccountName")?.StringValue;
            var displayName = attrSet.GetAttribute("displayName")?.StringValue;
            var email = attrSet.GetAttribute("mail")?.StringValue;
            var department = attrSet.GetAttribute("department")?.StringValue;
            var title = attrSet.GetAttribute("title")?.StringValue;

            var uacRaw = attrSet.GetAttribute("userAccountControl")?.StringValue ?? "0";
            var uac = int.TryParse(uacRaw, out var parsed) ? parsed : 0;

            var groups = new List<string>();
            var memberOf = attrSet.GetAttribute("memberOf");

            if (memberOf != null)
            {
                foreach (var dn in memberOf.StringValueArray)
                {
                    var cn = dn
                        .Split(',')
                        .FirstOrDefault(p => p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                        ?.Substring(3);

                    if (!string.IsNullOrWhiteSpace(cn))
                        groups.Add(cn);
                }
            }

            return new AdUserDto
            {
                Username = username,
                Upn = upn,
                DisplayName = displayName,
                Email = email,
                Department = department,
                Title = title,
                Enabled = (uac & 0x2) == 0,
                Groups = groups
            };
        }

        return null;
    }

    private async Task<GraphUserInfo?> GetGraphUserByIdAsync(string objectId, CancellationToken ct)
    {
        var token = await GetGraphAccessTokenAsync(ct);

        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(objectId)}?$select=id,displayName,mail,userPrincipalName");

        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await _httpClient.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Graph por ID falhou. Status={Status} Body={Body}", (int)resp.StatusCode, body);
            return null;
        }

        return JsonSerializer.Deserialize<GraphUserInfo>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    private async Task<GraphUserInfo?> GetGraphUserByUpnAsync(string upn, CancellationToken ct)
    {
        var token = await GetGraphAccessTokenAsync(ct);

        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(upn)}?$select=id,displayName,mail,userPrincipalName");

        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await _httpClient.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Graph por UPN falhou. Status={Status} Body={Body}", (int)resp.StatusCode, body);
            return null;
        }

        return JsonSerializer.Deserialize<GraphUserInfo>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    private async Task<string> GetGraphAccessTokenAsync(CancellationToken ct)
    {
        var credential = new ClientSecretCredential(
            _azure.TenantId,
            _azure.ClientId,
            _azure.ClientSecret);

        var token = await credential.GetTokenAsync(
            new TokenRequestContext(new[] { "https://graph.microsoft.com/.default" }),
            ct);

        return token.Token;
    }

    private ClientPrincipalData? TryDecodeClientPrincipal(string value)
    {
        try
        {
            var bytes = Convert.FromBase64String(value);
            var json = Encoding.UTF8.GetString(bytes);

            return JsonSerializer.Deserialize<ClientPrincipalData>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao decodificar X-MS-CLIENT-PRINCIPAL.");
            return null;
        }
    }

    private static string Escape(string v) =>
        v.Replace("\\", "\\5c")
         .Replace("*", "\\2a")
         .Replace("(", "\\28")
         .Replace(")", "\\29")
         .Replace("\0", "\\00");

    private static AdAuthResult Fail(string error) =>
        new()
        {
            Authenticated = false,
            Error = error,
            Source = "AD Auth"
        };

    private sealed class GraphUserInfo
    {
        public string? Id { get; set; }
        public string? DisplayName { get; set; }
        public string? Mail { get; set; }
        public string? UserPrincipalName { get; set; }
    }

    private sealed class ClientPrincipalData
    {
        public string? IdentityProvider { get; set; }
        public string? UserId { get; set; }
        public string? UserDetails { get; set; }
        public List<ClientPrincipalClaim> Claims { get; set; } = new();

        public string? FindClaim(string type) =>
            Claims.FirstOrDefault(c => string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private sealed class ClientPrincipalClaim
    {
        public string? Type { get; set; }
        public string? Value { get; set; }
    }
}