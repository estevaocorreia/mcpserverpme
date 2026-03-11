using System.ComponentModel;
using System.Diagnostics;
using Mcpserver.Application.Services;
using Mcpserver.Domain.Contracts.Alarms;
using Mcpserver.Shared.Observability;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Mcpserver.Tools;

public sealed class AlarmTools
{
    private readonly AlarmService _service;
    public AlarmTools(AlarmService service) => _service = service;

    [McpServerTool]
    [Description("Consulta alarmes e eventos por período e lista de medidores (DisplayName). Usa a view vAlarmEventDetails com a SP sp_MEMT_LLM_Alarmes_EE.")]
    public async Task<AlarmEventResult> GetAlarmEvents(
        RequestContext<CallToolRequestParams> context,
        [Description("Parâmetros da consulta (Inicio, Fim, MultipleSources, ApenasAtivos)")] AlarmEventRequest req,
        CancellationToken ct)
    {
        
        var userId = context.Server.ClientInfo?.Name ?? "unknown";
        var userAgent = context.Server.ClientInfo?.Version ?? "unknown";

        using var activity = McpMetrics.ActivitySource.StartActivity("get_alarm_events");
        var sw = Stopwatch.StartNew();
        try
        {
            McpMetrics.ToolCalls.Add(1, new TagList
            {
                { "tool", "get_alarm_events" },
                { "user", userId }
            });

            activity?.SetTag("user.id", userId);
            activity?.SetTag("user.agent", userAgent);
            activity?.SetTag("inicio", req.Inicio);
            activity?.SetTag("fim", req.Fim);
            activity?.SetTag("apenas_ativos", req.ApenasAtivos);
            activity?.SetTag("has_filter", req.MultipleSources is not null);
            activity?.SetTag("sources_count", req.MultipleSources?.Count() ?? 0);

            var result = await _service.GetEventsAsync(req, ct);

            McpMetrics.ToolRowsReturned.Record(result.Returned, new TagList
            {
                { "tool", "get_alarm_events" },
                { "user", userId }
            });
            activity?.SetTag("returned", result.Returned);

            return result;
        }
        catch (Exception ex)
        {
            McpMetrics.ToolErrors.Add(1, new TagList
            {
                { "tool",  "get_alarm_events" },
                { "user",  userId },
                { "error", ex.GetType().Name }
            });
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            return new AlarmEventResult
            {
                Returned = 0,
                Rows = [],
                Source = "ERROR: " + ex.Message
            };
        }
        finally
        {
            var ms = sw.Elapsed.TotalMilliseconds;
            McpMetrics.ToolDuration.Record(ms, new TagList
            {
                { "tool", "get_alarm_events" },
                { "user", userId }
            });
            activity?.SetTag("duration_ms", ms);
        }
    }
}