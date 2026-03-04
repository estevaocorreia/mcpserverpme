using System.ComponentModel;
using System.Diagnostics;
using Mcpserver.Application.Services;
using Mcpserver.Domain.Contracts.Alarms;
using Mcpserver.Shared.Observability;
using ModelContextProtocol.Server;

namespace Mcpserver.Tools;

public sealed class AlarmTools
{
    private readonly AlarmService _service;

    public AlarmTools(AlarmService service) => _service = service;

    [McpServerTool]
    [Description("Consulta alarmes e eventos por período e lista de medidores (DisplayName). Usa a view vAlarmEventDetails com a SP sp_MEMT_LLM_Alarmes_EE.")]
    public async Task<AlarmEventResult> GetAlarmEvents(
        [Description("Parâmetros da consulta (Inicio, Fim, MultipleSources, ApenasAtivos)")] AlarmEventRequest req,
        CancellationToken ct)
    {
        using var activity = McpMetrics.ActivitySource.StartActivity("get_alarm_events");
        var sw = Stopwatch.StartNew();
        try
        {
            McpMetrics.ToolCalls.Add(1, new TagList { { "tool", "get_alarm_events" } });

            activity?.SetTag("inicio", req.Inicio);
            activity?.SetTag("fim", req.Fim);
            activity?.SetTag("apenas_ativos", req.ApenasAtivos);
            activity?.SetTag("has_filter", req.MultipleSources is not null);

            var result = await _service.GetEventsAsync(req, ct);

            McpMetrics.ToolRowsReturned.Record(result.Returned, new TagList { { "tool", "get_alarm_events" } });
            activity?.SetTag("returned", result.Returned);

            return result;
        }
        catch (Exception ex)
        {
            McpMetrics.ToolErrors.Add(1, new TagList { { "tool", "get_alarm_events" }, { "error", ex.GetType().Name } });
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
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
            McpMetrics.ToolDuration.Record(ms, new TagList { { "tool", "get_alarm_events" } });
            activity?.SetTag("duration_ms", ms);
        }
    }
}