using System.DirectoryServices.Protocols;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Mcpserver.Application.Interfaces;
using Mcpserver.Domain.Contracts.Auth;
using Mcpserver.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Mcpserver.Infrastructure.Services;

public sealed class AdAuthService : IAdAuthService
{
    private readonly AzureAdSettings _azure;
    private readonly ActiveDirectorySettings _ad;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AdAuthService> _logger;

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
    }

    public async Task<AdAuthResult> AuthenticateAsync(AdAuthRequest req, CancellationToken ct)
    {
        var accessToken = ResolveAccessToken(req);

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            _logger.LogWarning("Autenticação falhou: token não encontrado no header Authorization nem no body.");
            return Fail("Token de acesso não encontrado no header Authorization nem no body.");
        }

        string upn;
        try
        {
            upn = await ValidateTeamsTokenAsync(accessToken, ct);

            _logger.LogInformation("Token validado com sucesso. UPN extraído: {Upn}", upn);
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Token inválido ou expirado.");
            return Fail($"Token inválido ou expirado: {ex.Message}");
        }

        var samAccount = upn.Split('@')[0];

        try
        {
            using var conn = CreateConnection();
            BindServiceAccount(conn);

            _logger.LogInformation(
                "Consultando usuário no AD. SamAccount={SamAccount} Upn={Upn} Host={Host} BaseDn={BaseDn}",
                samAccount, upn, _ad.Host, _ad.BaseDn);

            var user = SearchUser(conn, samAccount, upn);

            if (user is null)
            {
                _logger.LogWarning(
                    "Usuário autenticado no token, mas não encontrado no AD. Upn={Upn} SamAccount={SamAccount}",
                    upn, samAccount);

                return Fail($"Usuário '{upn}' autenticado, mas não encontrado no AD.");
            }

            if (!user.Enabled)
            {
                _logger.LogWarning(
                    "Usuário encontrado no AD, mas conta está desativada. Upn={Upn} SamAccount={SamAccount}",
                    upn, samAccount);

                return Fail($"Conta '{samAccount}' está desativada no Active Directory.");
            }

            _logger.LogInformation(
                "Usuário autenticado com sucesso. Upn={Upn} Nome={DisplayName} Email={Email} Departamento={Department} Cargo={Title} Grupos={GroupsCount}",
                user.Upn,
                user.DisplayName,
                user.Email,
                user.Department,
                user.Title,
                user.Groups.Count);

            if (user.Groups.Count > 0)
            {
                _logger.LogInformation(
                    "Grupos do usuário {Upn}: {Groups}",
                    user.Upn,
                    string.Join(", ", user.Groups));
            }

            return new AdAuthResult
            {
                Authenticated = true,
                User = user,
                Source = "Bearer Token → AD on-premises"
            };
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "Erro LDAP ao autenticar usuário. Upn={Upn}", upn);
            return Fail($"Erro LDAP ({ex.ErrorCode}): {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao autenticar usuário. Upn={Upn}", upn);
            return Fail($"Erro inesperado: {ex.Message}");
        }
    }

    private string? ResolveAccessToken(AdAuthRequest? req)
    {
        var authHeader = _httpContextAccessor.HttpContext?
            .Request.Headers.Authorization
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(authHeader) &&
            authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Token encontrado no header Authorization.");
            return authHeader["Bearer ".Length..].Trim();
        }

        if (!string.IsNullOrWhiteSpace(req?.TeamsToken))
        {
            _logger.LogDebug("Token encontrado no body como fallback.");
            return req.TeamsToken.Trim();
        }

        return null;
    }

    private async Task<string> ValidateTeamsTokenAsync(string token, CancellationToken ct)
    {
        var metadataUrl = $"https://login.microsoftonline.com/{_azure.TenantId}/v2.0/.well-known/openid-configuration";

        var configMgr = new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataUrl,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever());

        var config = await configMgr.GetConfigurationAsync(ct);

        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers =
            [
                $"https://login.microsoftonline.com/{_azure.TenantId}/v2.0",
                $"https://sts.windows.net/{_azure.TenantId}/"
            ],
            ValidateAudience = true,
            ValidAudience = _azure.ClientId,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = config.SigningKeys,
            ClockSkew = TimeSpan.FromMinutes(5)
        };

        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(token, validationParams, out _);

        var upn =
            principal.FindFirst("upn")?.Value ??
            principal.FindFirst("preferred_username")?.Value ??
            principal.FindFirst("email")?.Value;

        if (string.IsNullOrWhiteSpace(upn))
            throw new SecurityTokenException("Token válido, mas não contém 'upn', 'preferred_username' ou 'email'.");

        return upn;
    }

    private LdapConnection CreateConnection()
    {
        var conn = new LdapConnection(new LdapDirectoryIdentifier(_ad.Host, _ad.Port))
        {
            AuthType = AuthType.Basic,
            Timeout = TimeSpan.FromSeconds(10)
        };

        conn.SessionOptions.ProtocolVersion = 3;

        if (_ad.UseSsl)
            conn.SessionOptions.SecureSocketLayer = true;

        return conn;
    }

    private void BindServiceAccount(LdapConnection conn) =>
        conn.Bind(new NetworkCredential(_ad.BindUser, _ad.BindPass));

    private AdUserDto? SearchUser(LdapConnection conn, string samAccount, string upn)
    {
        var filter = $"(&(objectClass=user)(|(sAMAccountName={Escape(samAccount)})(userPrincipalName={Escape(upn)})))";
        var attrs = new[] { "sAMAccountName", "displayName", "mail", "department", "title", "memberOf", "userAccountControl" };

        var resp = (SearchResponse)conn.SendRequest(
            new SearchRequest(_ad.BaseDn, filter, SearchScope.Subtree, attrs));

        if (resp.Entries.Count == 0)
            return null;

        var e = resp.Entries[0];
        var uac = int.Parse(e.Attributes["userAccountControl"]?[0]?.ToString() ?? "0");
        var groups = new List<string>();

        if (e.Attributes["memberOf"] is not null)
        {
            foreach (var dn in e.Attributes["memberOf"])
            {
                var cn = dn.ToString()!
                    .Split(',')
                    .FirstOrDefault(p => p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                    ?.Substring(3);

                if (cn is not null)
                    groups.Add(cn);
            }
        }

        return new AdUserDto
        {
            Username = e.Attributes["sAMAccountName"]?[0]?.ToString(),
            Upn = upn,
            DisplayName = e.Attributes["displayName"]?[0]?.ToString(),
            Email = e.Attributes["mail"]?[0]?.ToString(),
            Department = e.Attributes["department"]?[0]?.ToString(),
            Title = e.Attributes["title"]?[0]?.ToString(),
            Enabled = (uac & 0x2) == 0,
            Groups = groups
        };
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
}