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
    [Description("Autentica automaticamente o usuário atual usando a conexão corporativa e retorna seus dados do Active Directory.")]
    public async Task<AdAuthResult> AD_Authenticate(
        RequestContext<CallToolRequestParams> context,
        CancellationToken ct)
    {
        var mcpClient = context.Server.ClientInfo?.Name ?? "unknown";

        _logger.LogInformation(
            "Chamada recebida na tool AD_Authenticate. ClienteMcp={McpClient}",
            mcpClient);

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

            var result = await _service.AuthenticateAsync(ct);

            _logger.LogInformation(
                "Resultado da autenticação. ClienteMcp={McpClient} Autenticado={Authenticated} Upn={Upn}",
                mcpClient,
                result.Authenticated,
                result.User?.Upn ?? "unknown");

            var upn = result.User?.Upn ?? "unknown";
            activity?.SetTag("authenticated", result.Authenticated);
            activity?.SetTag("user.upn", upn);
            activity?.SetTag("user.name", result.User?.DisplayName);

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