namespace Mcpserver.Domain.Contracts.Meters;

public sealed class MeterEventsByClassificationResult
{
    public int Returned { get; set; }
    public List<MeterEventsByClassificationRow> Items { get; set; } = new();
    public string Source { get; set; } = "";
}