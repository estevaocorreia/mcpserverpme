namespace Mcpserver.Domain.Contracts.Alarms;

public sealed class AlarmQueryResult
{
    public int Returned { get; init; }
    public IReadOnlyList<Dictionary<string, object?>> Rows { get; init; } = Array.Empty<Dictionary<string, object?>>();
}
