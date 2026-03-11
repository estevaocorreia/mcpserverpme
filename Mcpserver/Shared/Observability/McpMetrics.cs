using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Mcpserver.Shared.Observability;

public static class McpMetrics
{
    public static readonly ActivitySource ActivitySource =
        new("Mcpserver.Tools");

    private static readonly Meter Meter =
        new("Mcpserver.Tools", "1.0.0");

    
    public static readonly Counter<long> ToolCalls =
        Meter.CreateCounter<long>("mcp.tool.calls", "calls", "Total de chamadas por tool");

  
    public static readonly Histogram<double> ToolDuration =
        Meter.CreateHistogram<double>("mcp.tool.duration", "ms", "Duração das chamadas por tool");

 
    public static readonly Counter<long> ToolErrors =
        Meter.CreateCounter<long>("mcp.tool.errors", "errors", "Total de erros por tool");

   
    public static readonly Histogram<long> ToolRowsReturned =
        Meter.CreateHistogram<long>("mcp.tool.rows_returned", "rows", "Registros retornados por tool");

    public static async Task<T> TrackAsync<T>(
        string toolName,
        string? userId,
        Func<Activity?, Task<T>> execute,
        int rowsReturned = 0)
    {
        var tags = new TagList
        {
            { "tool", toolName },
            { "user", userId ?? "anonymous" }
        };

        using var activity = ActivitySource.StartActivity(
            $"mcp.tool.{toolName}",
            ActivityKind.Server);

        activity?.SetTag("tool.name", toolName);
        activity?.SetTag("user.id", userId ?? "anonymous");

        ToolCalls.Add(1, tags);

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await execute(activity);

            sw.Stop();
            ToolDuration.Record(sw.Elapsed.TotalMilliseconds, tags);

            if (rowsReturned > 0)
                ToolRowsReturned.Record(rowsReturned, tags);

            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            ToolDuration.Record(sw.Elapsed.TotalMilliseconds, tags);
            ToolErrors.Add(1, tags);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            throw;
        }
    }

    private static long _toolCallsSnapshot = 0;
    private static long _toolErrorsSnapshot = 0;

    public static long ToolCallsSnapshot => Interlocked.Read(ref _toolCallsSnapshot);
    public static long ToolErrorsSnapshot => Interlocked.Read(ref _toolErrorsSnapshot);


}