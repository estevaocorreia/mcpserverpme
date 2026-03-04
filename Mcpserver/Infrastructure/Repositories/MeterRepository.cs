using Dapper;
using Mcpserver.Application.Interfaces;
using Mcpserver.Domain.Contracts.Meters;
using Mcpserver.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;
using System.Text.Json;

namespace Mcpserver.Infrastructure.Repositories;

public sealed class MeterRepository : IMeterRepository
{
    private readonly string _connString;

    public MeterRepository(IConfiguration config)
    {
        _connString = config.GetConnectionString("IonData")
            ?? throw new InvalidOperationException("ConnectionStrings:IonData não configurada.");
    }

    private IDbConnection CreateConn() => new SqlConnection(_connString);

    public async Task<MeterListResult> ListAsync(MeterListRequest request, CancellationToken ct)
    {
        var take = Math.Clamp(request.Take, 1, 1000);
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : $"%{request.Search.Trim()}%";

        const string sql = """
    SELECT TOP (@take)
        s.[ID] AS Id,
        s.[Name] AS Name,
        s.[NamespaceID] AS NamespaceId,
        s.[SourceTypeID] AS SourceTypeId,
        st.[Name] AS SourceTypeName,
        s.[TimeZoneID] AS TimeZoneId,
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

    public async Task<MeterEnergyResult> Meter_EnergyAsync(MeterEnergyQuery query, CancellationToken ct)
    {
        if (query.Inicio >= query.Fim)
            throw new ArgumentException("Inicio deve ser menor que Fim.");

        var names = (query.DisplayNames ?? new List<string>())
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(query.Take, 1, 5000))
            .ToList();

        if (names.Count == 0)
            throw new ArgumentException("DisplayNames não pode estar vazio. Envie ao menos 1 medidor (DisplayName).");

        var payload = JsonSerializer.Serialize(new { SourceIdList = names });

        using var conn = CreateConn();

        var p = new DynamicParameters();
        p.Add("@Inicio", query.Inicio, DbType.DateTime);
        p.Add("@Fim", query.Fim, DbType.DateTime);
        p.Add("@MultipleSources", payload, DbType.String);

        var rows = (await conn.QueryAsync<MeterEnergyRow>(
            new CommandDefinition(
                "dbo.sp_MEMT_LLM_Consumo_EE",
                p,
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct
            )
        )).AsList();

        return new MeterEnergyResult
        {
            Returned = rows.Count,
            Items = rows,
            Source = "ION_Data.dbo.sp_MEMT_LLM_Consumo_EE"
        };
    }

    public async Task<MeterDemandResult> Meter_DemandAsync(MeterDemandQuery query, CancellationToken ct)
    {
        if (query.Inicio >= query.Fim)
            throw new ArgumentException("Inicio deve ser menor que Fim.");

        var names = (query.DisplayNames ?? new List<string>())
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(query.Take, 1, 5000))
            .ToList();

        if (names.Count == 0)
            throw new ArgumentException("DisplayNames não pode estar vazio. Envie ao menos 1 medidor (DisplayName).");

        var payload = JsonSerializer.Serialize(new { SourceIdList = names });

        using var conn = CreateConn();

        var p = new DynamicParameters();
        p.Add("@Inicio", query.Inicio, DbType.DateTime);
        p.Add("@Fim", query.Fim, DbType.DateTime);
        p.Add("@MultipleSources", payload, DbType.String);

        var rows = (await conn.QueryAsync<MeterDemandRow>(
            new CommandDefinition(
                "dbo.sp_MEMT_LLM_Demanda_EE",
                p,
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct
            )
        )).AsList();

        return new MeterDemandResult
        {
            Returned = rows.Count,
            Items = rows,
            Source = "ION_Data.dbo.sp_MEMT_LLM_Demanda_EE"
        };
    }



    public async Task<MeterEventsByClassificationResult> Meter_EventsByClassificationAsync(
    MeterEventsByClassificationQuery query, CancellationToken ct)
    {
        var take = Math.Clamp(query.Take, 1, 50000);

        var p = new DynamicParameters();
        p.Add("@inicio", query.InicioUtc, DbType.DateTime);
        p.Add("@fim", query.FimUtc, DbType.DateTime);

        // Monta SQL com filtros opcionais
        var sql = """
    SELECT TOP (@take)
        S.DisplayName AS DisplayName,
        vPQ.Classification AS Classification,
        COUNT(*) AS [Count]
    FROM [ION_Data].[dbo].[vPQ_Events] vPQ
    INNER JOIN [ION_Data].[dbo].[Source] S ON vPQ.SourceID = S.ID
    WHERE vPQ.DatalogTimestampUtc >= @inicio
      AND (@fim IS NULL OR vPQ.DatalogTimestampUtc <= @fim)
    """;

        p.Add("@take", take);

        // filtro por DisplayName (IN)
        if (query.DisplayNames is { Count: > 0 })
        {
            var names = query.DisplayNames
                .Select(x => (x ?? "").Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count > 0)
            {
                sql += " AND S.DisplayName IN @names";
                p.Add("@names", names);
            }
        }

        // filtro por Classification (IN)
        if (query.Classifications is { Count: > 0 })
        {
            var cls = query.Classifications
                .Select(x => (x ?? "").Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (cls.Count > 0)
            {
                sql += " AND vPQ.Classification IN @cls";
                p.Add("@cls", cls);
            }
        }

        sql += """
    GROUP BY S.DisplayName, vPQ.Classification
    ORDER BY S.DisplayName, vPQ.Classification;
    """;

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

    }
