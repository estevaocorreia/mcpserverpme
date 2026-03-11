using Dapper;
using Mcpserver.Application.Interfaces;
using Mcpserver.Domain.Contracts.Alarms;
using Mcpserver.Shared.Observability;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;

namespace Mcpserver.Infrastructure.Repositories;

public sealed class AlarmRepository : IAlarmRepository
{
    private readonly string _connString;
    private IReadOnlySet<string>? _columnCache;

    public AlarmRepository(IConfiguration config)
    {
        _connString = config.GetConnectionString("IonData")
          ?? throw new InvalidOperationException("ConnectionStrings:IonData não configurada.");
    }

    private IDbConnection CreateConn() => new SqlConnection(_connString);

    
    public async Task<IReadOnlyList<string>> GetColumnsAsync(CancellationToken ct)
    {
        await EnsureColumnCacheAsync(ct);
        return _columnCache!.OrderBy(c => c).ToArray();
    }


    public Task<AlarmQueryResult> QueryAsync(AlarmQueryRequest request, CancellationToken ct)
        => DbActivity.TrackAsync(
            operationName: "alarm_query",
            dbStatement: "SELECT * FROM [ION_Data].[dbo].[Alarm]",
            dbName: "ION_Data",
            execute: () => QueryInternalAsync(request, ct),
            rowsSelector: r => r.Returned);

  
    public Task<AlarmEventResult> GetEventsAsync(AlarmEventRequest request, CancellationToken ct)
        => DbActivity.TrackAsync(
            operationName: "sp_MEMT_LLM_Alarmes_EE",
            dbStatement: "EXEC dbo.sp_MEMT_LLM_Alarmes_EE",
            dbName: "ION_Data",
            execute: () => GetEventsInternalAsync(request, ct),
            rowsSelector: r => r.Returned);


    private async Task<AlarmQueryResult> QueryInternalAsync(AlarmQueryRequest request, CancellationToken ct)
    {
        await EnsureColumnCacheAsync(ct);

        var take = Math.Clamp(request.Take, 1, 1000);
        var skip = Math.Max(0, request.Skip);
        var orderBy = request.OrderBy;
        var dateCol = request.DateColumn;

        if (!string.IsNullOrWhiteSpace(orderBy) && !_columnCache!.Contains(orderBy))
            throw new ArgumentException($"OrderBy inválido: '{orderBy}'.");
        if (!string.IsNullOrWhiteSpace(dateCol) && !_columnCache!.Contains(dateCol))
            throw new ArgumentException($"DateColumn inválida: '{dateCol}'.");

        var sql = "SELECT * FROM [ION_Data].[dbo].[Alarm] WHERE 1=1";
        var p = new DynamicParameters();

        int i = 0;
        foreach (var kv in request.EqualFilters)
        {
            if (string.IsNullOrWhiteSpace(kv.Key)) continue;
            if (!_columnCache!.Contains(kv.Key))
                throw new ArgumentException($"Coluna inválida em Equals: '{kv.Key}'.");
            var pn = $"@eq{i++}";
            sql += $" AND [{kv.Key}] = {pn}";
            p.Add(pn, kv.Value);
        }

        if (!string.IsNullOrWhiteSpace(dateCol))
        {
            if (request.FromUtc is not null) { sql += $" AND [{dateCol}] >= @fromUtc"; p.Add("@fromUtc", request.FromUtc.Value); }
            if (request.ToUtc is not null) { sql += $" AND [{dateCol}] <= @toUtc"; p.Add("@toUtc", request.ToUtc.Value); }
        }

        if (!string.IsNullOrWhiteSpace(request.ContainsText))
        {
            p.Add("@txt", $"%{request.ContainsText.Trim()}%");
            var textCols = await ResolveTextColumnsAsync(request.TextColumns, ct);
            if (textCols.Count > 0)
                sql += " AND (" + string.Join(" OR ", textCols.Select(c => $"[{c}] LIKE @txt")) + ")";
        }

        if (!string.IsNullOrWhiteSpace(orderBy))
            sql += $" ORDER BY [{orderBy}] {(request.Desc ? "DESC" : "ASC")}";

        sql += " OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;";
        p.Add("@skip", skip);
        p.Add("@take", take);

        using var conn = CreateConn();
        var rows = await conn.QueryAsync(sql, p);

        var list = rows.Select(r =>
            ((IDictionary<string, object?>)r).ToDictionary(k => k.Key, v => v.Value)
        ).ToList();

        return new AlarmQueryResult { Returned = list.Count, Rows = list };
    }

    private async Task<AlarmEventResult> GetEventsInternalAsync(AlarmEventRequest request, CancellationToken ct)
    {
        const string sp = "dbo.sp_MEMT_LLM_Alarmes_EE";

        var p = new DynamicParameters();
        p.Add("@Inicio", request.Inicio, DbType.DateTime);
        p.Add("@Fim", request.Fim, DbType.DateTime);
        p.Add("@MultipleSources", request.MultipleSources);
        p.Add("@ApenasAtivos", request.ApenasAtivos ? 1 : 0, DbType.Int32);
        p.Add("@Debug", 0, DbType.Int32);

        using var conn = CreateConn();
        var rows = await conn.QueryAsync(
            new CommandDefinition(sp, p, commandType: CommandType.StoredProcedure, cancellationToken: ct));

        var list = rows.Select(r =>
            ((IDictionary<string, object?>)r).ToDictionary(k => k.Key, v => v.Value)
        ).ToList();

        return new AlarmEventResult { Returned = list.Count, Rows = list, Source = sp };
    }

   
    private async Task EnsureColumnCacheAsync(CancellationToken ct)
    {
        if (_columnCache is not null) return;
        const string sql = """
            SELECT c.name FROM sys.columns c
            INNER JOIN sys.objects o ON c.object_id = o.object_id
            INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
            WHERE o.name = 'Alarm' AND s.name = 'dbo'
            """;
        using var conn = CreateConn();
        var cols = await conn.QueryAsync<string>(new CommandDefinition(sql, cancellationToken: ct));
        _columnCache = cols.Select(x => x.Trim()).Where(x => x.Length > 0)
                           .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<List<string>> ResolveTextColumnsAsync(string[]? requested, CancellationToken ct)
    {
        await EnsureColumnCacheAsync(ct);
        if (requested is { Length: > 0 })
            return requested.Where(c => _columnCache!.Contains(c)).ToList();

        const string sql = """
            SELECT c.name FROM sys.columns c
            INNER JOIN sys.objects o ON c.object_id = o.object_id
            INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
            INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
            WHERE o.name = 'Alarm' AND s.name = 'dbo'
              AND t.name IN ('varchar','nvarchar','char','nchar','text','ntext')
            """;
        using var conn = CreateConn();
        var cols = await conn.QueryAsync<string>(new CommandDefinition(sql, cancellationToken: ct));
        return cols.Select(x => x.Trim()).Where(x => x.Length > 0)
                   .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}