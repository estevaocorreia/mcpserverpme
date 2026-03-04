using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Mcpserver.Shared.Observability;

public static class McpMetrics
{
    public static readonly ActivitySource ActivitySource =
        new("Mcpserver.Tools");

    private static readonly Meter Meter =
        new("Mcpserver.Tools", "1.0.0");

    // Contagem de chamadas por tool
    public static readonly Counter<long> ToolCalls =
        Meter.CreateCounter<long>("mcp.tool.calls", "calls", "Total de chamadas por tool");

    // Latência por tool
    public static readonly Histogram<double> ToolDuration =
        Meter.CreateHistogram<double>("mcp.tool.duration", "ms", "Duração das chamadas por tool");

    // Erros por tool
    public static readonly Counter<long> ToolErrors =
        Meter.CreateCounter<long>("mcp.tool.errors", "errors", "Total de erros por tool");

    // Registros retornados
    public static readonly Histogram<long> ToolRowsReturned =
        Meter.CreateHistogram<long>("mcp.tool.rows_returned", "rows", "Registros retornados por tool");
} 