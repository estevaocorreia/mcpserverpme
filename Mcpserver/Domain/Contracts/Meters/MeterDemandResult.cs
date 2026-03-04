namespace Mcpserver.Domain.Contracts.Meters;

public sealed class MeterDemandResult
{
    public int Returned { get; set; }
    public List<MeterDemandRow> Items { get; set; } = new();
    public string Source { get; set; } = "";
}