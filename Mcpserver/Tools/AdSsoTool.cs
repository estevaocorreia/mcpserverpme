using Mcpserver.Application.Interfaces;
using Mcpserver.Domain.Contracts.Auth;
using Mcpserver.Shared.Observability;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;

namespace Mcpserver.Tools;

public sealed class AdSsoTool
{
    private readonly IAdAuthService _service;
    private readonly ILogger<AdSsoTool> _logger;

    public AdSsoTool(IAdAuthService service, ILogger<AdSsoTool> logger)
    {
        _service = service;
        _logger = logger;
    }

    [McpServerTool]
    [Description("Autentica o usuário via token Bearer enviado na requisição. Se necessário, aceita fallback pelo body.")]
    public async Task<AdAuthResult> AD_Authenticate(
        RequestContext<CallToolRequestParams> context,
        [Description("Opcional. Fallback para testes manuais quando não houver header Authorization.")] AdAuthRequest? req,
        CancellationToken ct)
    {
        req ??= new AdAuthRequest();

        var mcpClient = context.Server.ClientInfo?.Name ?? "unknown";

        _logger.LogInformation(
            "Chamada recebida na tool AD_Authenticate. ClienteMcp={McpClient} TemTokenNoBody={HasBodyToken}",
            mcpClient,
            !string.IsNullOrWhiteSpace(req.TeamsToken));

        using var activity = McpMetrics.ActivitySource.StartActivity("ad_authenticate");
        var sw = Stopwatch.StartNew();

        try
        {
            McpMetrics.ToolCalls.Add(1, new TagList
            {
                { "tool", "ad_authenticate" },
                { "user", mcpClient }
            });

            activity?.SetTag("mcp.client", mcpClient);
            activity?.SetTag("has_body_token", !string.IsNullOrWhiteSpace(req.TeamsToken));

            var result = await _service.AuthenticateAsync(req, ct);

            _logger.LogInformation(
                "Resultado da autenticação. ClienteMcp={McpClient} Autenticado={Authenticated} Upn={Upn}",
                mcpClient,
                result.Authenticated,
                result.User?.Upn ?? "unknown");

            var upn = result.User?.Upn ?? "unknown";
            activity?.SetTag("authenticated", result.Authenticated);
            activity?.SetTag("user.upn", upn);
            activity?.SetTag("user.name", result.User?.DisplayName);

            McpMetrics.ToolCalls.Add(0, new TagList
            {
                { "tool", "ad_authenticate" },
                { "user", upn },
                { "authenticated", result.Authenticated }
            });

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro na tool AD_Authenticate. ClienteMcp={McpClient}", mcpClient);

            McpMetrics.ToolErrors.Add(1, new TagList
            {
                { "tool", "ad_authenticate" },
                { "user", mcpClient },
                { "error", ex.GetType().Name }
            });

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);

            return new AdAuthResult
            {
                Authenticated = false,
                Error = ex.Message,
                Source = "ERROR"
            };
        }
        finally
        {
            var ms = sw.Elapsed.TotalMilliseconds;

            McpMetrics.ToolDuration.Record(ms, new TagList
            {
                { "tool", "ad_authenticate" },
                { "user", mcpClient }
            });

            activity?.SetTag("duration_ms", ms);
        }
    }
}