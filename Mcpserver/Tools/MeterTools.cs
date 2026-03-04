using System.ComponentModel;
using System.Diagnostics;
using Mcpserver.Application.Interfaces;
using Mcpserver.Domain.Contracts.Meters;
using Mcpserver.Shared.Observability;
using ModelContextProtocol.Server;

namespace Mcpserver.Tools;

public sealed class MeterTools
{
    private readonly IMeterService _service;

    public MeterTools(IMeterService service) => _service = service;

    [McpServerTool]
    [Description("Meter: lista medidores (ION_Data.dbo.Source) com SourceType (ION_Data.dbo.SourceType). Suporta filtro por texto e paginação.")]
    public async Task<MeterListResult> Meter_List(
        [Description("Parâmetros de listagem")] MeterListRequest? req,
        CancellationToken ct)
    {
        req ??= new MeterListRequest();
        using var activity = McpMetrics.ActivitySource.StartActivity("meter_list");
        var sw = Stopwatch.StartNew();
        try
        {
            McpMetrics.ToolCalls.Add(1, new TagList { { "tool", "meter_list" } });

            var result = await _service.ListAsync(req, ct);

            McpMetrics.ToolRowsReturned.Record(result.Returned, new TagList { { "tool", "meter_list" } });
            activity?.SetTag("returned", result.Returned);

            return result;
        }
        catch (Exception ex)
        {
            McpMetrics.ToolErrors.Add(1, new TagList { { "tool", "meter_list" }, { "error", ex.GetType().Name } });
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return new MeterListResult { Returned = 0, Items = [], Source = "ERROR: " + ex.Message };
        }
        finally
        {
            var ms = sw.Elapsed.TotalMilliseconds;
            McpMetrics.ToolDuration.Record(ms, new TagList { { "tool", "meter_list" } });
            activity?.SetTag("duration_ms", ms);
        }
    }

    [McpServerTool]
    [Description("Meter: retorna os medidores agrupados por 'grupo' (texto antes do primeiro '.' do DisplayName; se vazio usa Name).")]
    public async Task<MeterGroupResult> Meter_ByGroup(
        [Description("Parâmetros de listagem (mesmos do Meter_List).")] MeterListRequest? req,
        CancellationToken ct)
    {
        req ??= new MeterListRequest();
        using var activity = McpMetrics.ActivitySource.StartActivity("meter_by_group");
        var sw = Stopwatch.StartNew();
        try
        {
            McpMetrics.ToolCalls.Add(1, new TagList { { "tool", "meter_by_group" } });

            var list = await _service.ListAsync(req, ct);

            static string GetGroup(MeterDetailsDto m)
            {
                var baseName = !string.IsNullOrWhiteSpace(m.DisplayName) ? m.DisplayName : m.Name;
                if (string.IsNullOrWhiteSpace(baseName)) return "SemGrupo";
                var idx = baseName.IndexOf('.');
                return idx <= 0 ? "SemGrupo" : baseName[..idx].Trim();
            }

            var dict = new Dictionary<string, List<MeterDetailsDto>>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in list.Items)
            {
                var g = GetGroup(m);
                if (!dict.TryGetValue(g, out var bucket)) { bucket = []; dict[g] = bucket; }
                bucket.Add(m);
            }

            var ordered = dict
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.OrderBy(x => string.IsNullOrWhiteSpace(x.DisplayName) ? x.Name : x.DisplayName).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var result = new MeterGroupResult
            {
                Groups = ordered.Count,
                Returned = list.Items.Count,
                ItemsByGroup = ordered,
                Source = list.Source + " | grouped by DisplayName prefix"
            };

            McpMetrics.ToolRowsReturned.Record(result.Returned, new TagList { { "tool", "meter_by_group" } });
            activity?.SetTag("groups", result.Groups);
            activity?.SetTag("returned", result.Returned);

            return result;
        }
        catch (Exception ex)
        {
            McpMetrics.ToolErrors.Add(1, new TagList { { "tool", "meter_by_group" }, { "error", ex.GetType().Name } });
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return new MeterGroupResult { Groups = 0, Returned = 0, ItemsByGroup = [], Source = "ERROR: " + ex.Message };
        }
        finally
        {
            var ms = sw.Elapsed.TotalMilliseconds;
            McpMetrics.ToolDuration.Record(ms, new TagList { { "tool", "meter_by_group" } });
            activity?.SetTag("duration_ms", ms);
        }
    }

    [McpServerTool]
    [Description("Meter: consulta consumo total (kWh) para uma lista de medidores (DisplayName) em período local. Datas: dd/MM/yyyy ou dd/MM/yyyy HH:mm.")]
    public async Task<MeterEnergyResult> Meter_Energy(
        [Description("Requisição (inicio, fim e displayNames).")] MeterEnergyRequest? req,
        CancellationToken ct)
    {
        req ??= new MeterEnergyRequest();
        using var activity = McpMetrics.ActivitySource.StartActivity("meter_energy");
        var sw = Stopwatch.StartNew();
        try
        {
            McpMetrics.ToolCalls.Add(1, new TagList { { "tool", "meter_energy" } });

            activity?.SetTag("inicio", req.Inicio);
            activity?.SetTag("fim", req.Fim);
            activity?.SetTag("meters_count", req.DisplayNames?.Count ?? 0);

            var result = await _service.Meter_EnergyAsync(req, ct);

            McpMetrics.ToolRowsReturned.Record(result.Returned, new TagList { { "tool", "meter_energy" } });
            activity?.SetTag("returned", result.Returned);

            return result;
        }
        catch (Exception ex)
        {
            McpMetrics.ToolErrors.Add(1, new TagList { { "tool", "meter_energy" }, { "error", ex.GetType().Name } });
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return new MeterEnergyResult { Returned = 0, Items = [], Source = "ERROR: " + ex.Message };
        }
        finally
        {
            var ms = sw.Elapsed.TotalMilliseconds;
            McpMetrics.ToolDuration.Record(ms, new TagList { { "tool", "meter_energy" } });
            activity?.SetTag("duration_ms", ms);
        }
    }

    [McpServerTool]
    [Description("Meter: consulta demanda (kW) para uma lista de medidores (DisplayName) em período local. Datas: dd/MM/yyyy ou dd/MM/yyyy HH:mm.")]
    public async Task<MeterDemandResult> Meter_Demand(
        [Description("Requisição (inicio, fim e displayNames).")] MeterDemandRequest? req,
        CancellationToken ct)
    {
        req ??= new MeterDemandRequest();
        using var activity = McpMetrics.ActivitySource.StartActivity("meter_demand");
        var sw = Stopwatch.StartNew();
        try
        {
            McpMetrics.ToolCalls.Add(1, new TagList { { "tool", "meter_demand" } });

            activity?.SetTag("inicio", req.Inicio);
            activity?.SetTag("fim", req.Fim);
            activity?.SetTag("meters_count", req.DisplayNames?.Count ?? 0);

            var result = await _service.Meter_DemandAsync(req, ct);

            McpMetrics.ToolRowsReturned.Record(result.Returned, new TagList { { "tool", "meter_demand" } });
            activity?.SetTag("returned", result.Returned);

            return result;
        }
        catch (Exception ex)
        {
            McpMetrics.ToolErrors.Add(1, new TagList { { "tool", "meter_demand" }, { "error", ex.GetType().Name } });
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return new MeterDemandResult { Returned = 0, Items = [], Source = "ERROR: " + ex.Message };
        }
        finally
        {
            var ms = sw.Elapsed.TotalMilliseconds;
            McpMetrics.ToolDuration.Record(ms, new TagList { { "tool", "meter_demand" } });
            activity?.SetTag("duration_ms", ms);
        }
    }





    [McpServerTool]
    [Description("Meter: Eventos por classificação (vPQ_Events), agrupado por DisplayName e Classification, com contagem. Datas em UTC.")]
    public async Task<MeterEventsByClassificationResult> Meter_EventsByClassification(
    [Description("InicioUtc obrigatório (dd/MM/yyyy HH:mm). FimUtc opcional.")] MeterEventsByClassificationRequest? req,
    CancellationToken ct)
    {
        try
        {
            req ??= new MeterEventsByClassificationRequest();
            return await _service.Meter_EventsByClassificationAsync(req, ct);
        }
        catch (Exception ex)
        {
            return new MeterEventsByClassificationResult
            {
                Returned = 0,
                Items = new List<MeterEventsByClassificationRow>(),
                Source = "ERROR: " + ex.Message
            };
        }
    }



}