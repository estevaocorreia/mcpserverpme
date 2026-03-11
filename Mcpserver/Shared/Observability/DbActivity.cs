using System.Diagnostics;

namespace Mcpserver.Shared.Observability;

public static class DbActivity
{

    public static Task<T> TrackAsync<T>(
        string operationName,
        string dbStatement,
        string dbName,
        Func<Task<T>> execute)
        => TrackCoreAsync(operationName, dbStatement, dbName, execute, null);

    public static Task<T> TrackAsync<T>(
        string operationName,
        string dbStatement,
        string dbName,
        Func<Task<T>> execute,
        Func<T, int> rowsSelector)
        => TrackCoreAsync(operationName, dbStatement, dbName, execute, rowsSelector);

    private static async Task<T> TrackCoreAsync<T>(
        string operationName,
        string dbStatement,
        string dbName,
        Func<Task<T>> execute,
        Func<T, int>? rowsSelector)
    {
        using var activity = McpMetrics.ActivitySource.StartActivity(
            $"db.{operationName}",
            ActivityKind.Client);

        activity?.SetTag("db.system", "sqlserver");
        activity?.SetTag("db.name", dbName);
        activity?.SetTag("db.operation", operationName);
        activity?.SetTag("db.statement", dbStatement);

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await execute();
            sw.Stop();

            var rows = rowsSelector?.Invoke(result) ?? -1;

            activity?.SetTag("db.duration_ms", Math.Round(sw.Elapsed.TotalMilliseconds, 2));
            activity?.SetTag("db.rows_affected", rows >= 0 ? rows : null);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            activity?.SetTag("db.duration_ms", Math.Round(sw.Elapsed.TotalMilliseconds, 2));
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            throw;
        }
    }
}