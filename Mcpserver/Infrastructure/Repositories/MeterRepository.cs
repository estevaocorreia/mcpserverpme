using Dapper;
using Mcpserver.Application.Interfaces;
using Mcpserver.Domain.Contracts.Meters;
using Mcpserver.Models;
using Mcpserver.Shared.Observability;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;
using System.Text.Json;

namespace Mcpserver.Infrastructure.Repositories;

public sealed class MeterRepository : IMeterRepository
{
    private readonly string _connString;
    private readonly string _connStringNetwork;

    public MeterRepository(IConfiguration config)
    {
        _connString = config.GetConnectionString("IonData")
            ?? throw new InvalidOperationException("ConnectionStrings:IonData não configurada.");
        _connStringNetwork = config.GetConnectionString("IonNetwork")
            ?? throw new InvalidOperationException("ConnectionStrings:IonNetwork não configurada.");
    }

    private IDbConnection CreateConn() => new SqlConnection(_connString);
    private IDbConnection CreateNetworkConn() => new SqlConnection(_connStringNetwork);

    
    public Task<MeterListResult> ListAsync(MeterListRequest request, CancellationToken ct)
        => DbActivity.TrackAsync(
            operationName: "meter_list",
            dbStatement: "SELECT TOP N FROM ION_Data.dbo.Source + SourceType",
            dbName: "ION_Data",
            execute: () => ListInternalAsync(request, ct),
            rowsSelector: r => r.Returned);


    public Task<MeterEnergyResult> Meter_EnergyAsync(MeterEnergyQuery query, CancellationToken ct)
        => DbActivity.TrackAsync(
            operationName: "sp_MEMT_LLM_Consumo_EE",
            dbStatement: "EXEC dbo.sp_MEMT_LLM_Consumo_EE",
            dbName: "ION_Data",
            execute: () => EnergyInternalAsync(query, ct),
            rowsSelector: r => r.Returned);

  
    public Task<MeterDemandResult> Meter_DemandAsync(MeterDemandQuery query, CancellationToken ct)
        => DbActivity.TrackAsync(
            operationName: "sp_MEMT_LLM_Demanda_EE",
            dbStatement: "EXEC dbo.sp_MEMT_LLM_Demanda_EE",
            dbName: "ION_Data",
            execute: () => DemandInternalAsync(query, ct),
            rowsSelector: r => r.Returned);


    public Task<MeterEventsByClassificationResult> Meter_EventsByClassificationAsync(
        MeterEventsByClassificationQuery query, CancellationToken ct)
        => DbActivity.TrackAsync(
            operationName: "meter_events_by_classification",
            dbStatement: "SELECT FROM ION_Data.dbo.vPQ_Events GROUP BY DisplayName, Classification",
            dbName: "ION_Data",
            execute: () => EventsByClassificationInternalAsync(query, ct),
            rowsSelector: r => r.Returned);

    public Task<MeterNetworkStatusResult> Meter_NetworkStatusAsync(
        MeterNetworkStatusRequest request, CancellationToken ct)
        => DbActivity.TrackAsync(
            operationName: "meter_network_status",
            dbStatement: "SELECT FROM ION_Network.dbo.vPMCDevice",
            dbName: "ION_Network",
            execute: () => NetworkStatusInternalAsync(request, ct),
            rowsSelector: r => r.Returned);

   
    private async Task<MeterListResult> ListInternalAsync(MeterListRequest request, CancellationToken ct)
    {
        var take = Math.Clamp(request.Take, 1, 1000);
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : $"%{request.Search.Trim()}%";

        const string sql = """
            SELECT TOP (@take)
                s.[ID]              AS Id,
                s.[Name]            AS Name,
                s.[NamespaceID]     AS NamespaceId,
                s.[SourceTypeID]    AS SourceTypeId,
                st.[Name]           AS SourceTypeName,
                s.[TimeZoneID]      AS TimeZoneId,
                COALESCE(s.[Description],  '') AS Description,
                COALESCE(s.[Signature],     '') AS Signature,
                COALESCE(s.[DisplayName],   '') AS DisplayName
            FROM [ION_Data].[dbo].[Source] s
            LEFT JOIN [ION_Data].[dbo].[SourceType] st ON st.[ID] = s.[SourceTypeID]
            WHERE (@search IS NULL OR s.[Name] LIKE @search OR s.[DisplayName] LIKE @search)
            ORDER BY s.[ID] DESC;
            """;

        using var conn = CreateConn();
        var items = (await conn.QueryAsync<MeterDetailsDto>(
            new CommandDefinition(sql, new { take, search }, cancellationToken: ct)
        )).AsList();

        return new MeterListResult
        {
            Returned = items.Count,
            Items = items,
            Source = "ION_Data.dbo.Source + SourceType"
        };
    }

    private async Task<MeterEnergyResult> EnergyInternalAsync(MeterEnergyQuery query, CancellationToken ct)
    {
        if (query.Inicio >= query.Fim)
            throw new ArgumentException("Inicio deve ser menor que Fim.");

        var names = (query.DisplayNames ?? new List<string>())
            .Select(x => (x ?? "").Trim()).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(query.Take, 1, 5000)).ToList();

        if (names.Count == 0)
            throw new ArgumentException("DisplayNames não pode estar vazio.");

        var payload = JsonSerializer.Serialize(new { SourceIdList = names });

        var p = new DynamicParameters();
        p.Add("@Inicio", query.Inicio, DbType.DateTime);
        p.Add("@Fim", query.Fim, DbType.DateTime);
        p.Add("@MultipleSources", payload, DbType.String);

        using var conn = CreateConn();
        var rows = (await conn.QueryAsync<MeterEnergyRow>(
            new CommandDefinition("dbo.sp_MEMT_LLM_Consumo_EE", p,
                commandType: CommandType.StoredProcedure, cancellationToken: ct)
        )).AsList();

        return new MeterEnergyResult
        {
            Returned = rows.Count,
            Items = rows,
            Source = "ION_Data.dbo.sp_MEMT_LLM_Consumo_EE"
        };
    }

    private async Task<MeterDemandResult> DemandInternalAsync(MeterDemandQuery query, CancellationToken ct)
    {
        if (query.Inicio >= query.Fim)
            throw new ArgumentException("Inicio deve ser menor que Fim.");

        var names = (query.DisplayNames ?? new List<string>())
            .Select(x => (x ?? "").Trim()).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(query.Take, 1, 5000)).ToList();

        if (names.Count == 0)
            throw new ArgumentException("DisplayNames não pode estar vazio.");

        var payload = JsonSerializer.Serialize(new { SourceIdList = names });

        var p = new DynamicParameters();
        p.Add("@Inicio", query.Inicio, DbType.DateTime);
        p.Add("@Fim", query.Fim, DbType.DateTime);
        p.Add("@MultipleSources", payload, DbType.String);

        using var conn = CreateConn();
        var rows = (await conn.QueryAsync<MeterDemandRow>(
            new CommandDefinition("dbo.sp_MEMT_LLM_Demanda_EE", p,
                commandType: CommandType.StoredProcedure, cancellationToken: ct)
        )).AsList();

        return new MeterDemandResult
        {
            Returned = rows.Count,
            Items = rows,
            Source = "ION_Data.dbo.sp_MEMT_LLM_Demanda_EE"
        };
    }

    private async Task<MeterEventsByClassificationResult> EventsByClassificationInternalAsync(
        MeterEventsByClassificationQuery query, CancellationToken ct)
    {
        var take = Math.Clamp(query.Take, 1, 50000);

        var p = new DynamicParameters();
        p.Add("@inicio", query.InicioUtc, DbType.DateTime);
        p.Add("@fim", query.FimUtc, DbType.DateTime);
        p.Add("@take", take);

        var sql = """
            SELECT TOP (@take)
                S.DisplayName       AS DisplayName,
                vPQ.Classification  AS Classification,
                COUNT(*)            AS [Count]
            FROM [ION_Data].[dbo].[vPQ_Events] vPQ
            INNER JOIN [ION_Data].[dbo].[Source] S ON vPQ.SourceID = S.ID
            WHERE vPQ.DatalogTimestampUtc >= @inicio
              AND (@fim IS NULL OR vPQ.DatalogTimestampUtc <= @fim)
            """;

        if (query.DisplayNames is { Count: > 0 })
        {
            var names = query.DisplayNames.Select(x => (x ?? "").Trim())
                .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (names.Count > 0) { sql += " AND S.DisplayName IN @names"; p.Add("@names", names); }
        }

        if (query.Classifications is { Count: > 0 })
        {
            var cls = query.Classifications.Select(x => (x ?? "").Trim())
                .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (cls.Count > 0) { sql += " AND vPQ.Classification IN @cls"; p.Add("@cls", cls); }
        }

        sql += " GROUP BY S.DisplayName, vPQ.Classification ORDER BY S.DisplayName, vPQ.Classification;";

        using var conn = CreateConn();
        var rows = (await conn.QueryAsync<MeterEventsByClassificationRow>(
            new CommandDefinition(sql, p, cancellationToken: ct)
        )).AsList();

        return new MeterEventsByClassificationResult
        {
            Returned = rows.Count,
            Items = rows,
            Source = "ION_Data.dbo.vPQ_Events + Source (GROUP BY DisplayName, Classification)"
        };
    }

    private async Task<MeterNetworkStatusResult> NetworkStatusInternalAsync(
        MeterNetworkStatusRequest request, CancellationToken ct)
    {
        var take = Math.Clamp(request.Take, 1, 50000);
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : $"%{request.Search.Trim()}%";
        var site = string.IsNullOrWhiteSpace(request.Site) ? null : request.Site.Trim();
        var enabled = request.Enabled;
        var connected = request.Connected;
        var siteStatusContains = string.IsNullOrWhiteSpace(request.SiteStatusContains) ? null : $"%{request.SiteStatusContains.Trim()}%";

        const string connectedSql = """
            (
                [Site Status] LIKE '%Online%'
                OR [Site Status] LIKE '%Connected%'
                OR [Site Status] LIKE '%Up%'
                OR [Site Status] LIKE '%OK%'
                OR [Site Status] LIKE '%Port Available%'
            )
            """;

        const string disconnectedSql = $"""
            (
                [Site Status] IS NULL
                OR LTRIM(RTRIM(CAST([Site Status] AS nvarchar(256)))) = ''
                OR NOT {connectedSql}
            )
            """;

        var sql = """
            SELECT TOP (@take)
                CAST([ID] AS bigint)                              AS Id,
                COALESCE(CAST([Name]        AS nvarchar(256)), '') AS Name,
                COALESCE(CAST([Type]        AS nvarchar(256)), '') AS Type,
                COALESCE(CAST([Address]     AS nvarchar(256)), '') AS Address,
                COALESCE(CAST([Site]        AS nvarchar(256)), '') AS Site,
                COALESCE(CAST([Site Status] AS nvarchar(256)), '') AS SiteStatus,
                COALESCE(CAST([Enabled]     AS nvarchar(10)),  '') AS EnabledText,
                COALESCE(CAST([Protocol]    AS nvarchar(256)), '') AS Protocol,
                COALESCE(CAST([Description] AS nvarchar(512)), '') AS Description
            FROM [ION_Network].[dbo].[vPMCDevice]
            WHERE 1=1
              AND (@site   IS NULL OR [Site]    = @site)
              AND (@enabled IS NULL OR [Enabled] = CASE WHEN @enabled = 1 THEN 'YES' ELSE 'NO' END)
              AND (@search  IS NULL OR [Name] LIKE @search OR [Address] LIKE @search OR [Description] LIKE @search)
              AND (@siteStatusContains IS NULL OR [Site Status] LIKE @siteStatusContains)
            """;

        if (connected.HasValue)
            sql += connected.Value ? $"\n AND {connectedSql}\n" : $"\n AND {disconnectedSql}\n";

        sql += "\n ORDER BY [Site], [Name];";

        using var conn = CreateNetworkConn();
        var rows = (await conn.QueryAsync<MeterNetworkStatusRow>(
            new CommandDefinition(sql, new { take, search, site, enabled, siteStatusContains }, cancellationToken: ct)
        )).AsList();

        foreach (var r in rows)
        {
            r.IsEnabled =
                r.EnabledText.Equals("YES", StringComparison.OrdinalIgnoreCase) ||
                r.EnabledText.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                r.EnabledText.Equals("TRUE", StringComparison.OrdinalIgnoreCase);

            var st = (r.SiteStatus ?? "").Trim();
            r.IsConnected =
                r.IsEnabled &&
                (st.Contains("online", StringComparison.OrdinalIgnoreCase) ||
                 st.Contains("connected", StringComparison.OrdinalIgnoreCase) ||
                 st.Contains("up", StringComparison.OrdinalIgnoreCase) ||
                 st.Contains("ok", StringComparison.OrdinalIgnoreCase) ||
                 st.Contains("port available", StringComparison.OrdinalIgnoreCase));
        }

        return new MeterNetworkStatusResult
        {
            Returned = rows.Count,
            Items = rows,
            Source = "ION_Network.dbo.vPMCDevice (Enabled YES/NO + Connected filter)"
        };
    }
}