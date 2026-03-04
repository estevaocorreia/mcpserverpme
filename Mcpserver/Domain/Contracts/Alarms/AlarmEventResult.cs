using Mcpserver.Domain.Contracts.Alarms;


namespace Mcpserver.Domain.Contracts.Alarms;

public sealed class AlarmEventResult
{
    public int Returned { get; init; }
    public IReadOnlyList<Dictionary<string, object?>> Rows { get; init; }
        = Array.Empty<Dictionary<string, object?>>();
    public string Source { get; init; } = string.Empty;
}
