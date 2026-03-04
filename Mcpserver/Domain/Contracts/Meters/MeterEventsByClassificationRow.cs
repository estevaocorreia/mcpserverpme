namespace Mcpserver.Domain.Contracts.Meters;

public sealed class MeterEventsByClassificationRow
{
    public string DisplayName { get; set; } = "";
    public string Classification { get; set; } = "";
    public long Count { get; set; }
}