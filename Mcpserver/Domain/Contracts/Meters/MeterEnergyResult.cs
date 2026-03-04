namespace Mcpserver.Domain.Contracts.Meters;

public sealed class MeterEnergyResult
{
    public int Returned { get; set; }
    public List<MeterEnergyRow> Items { get; set; } = new();
    public string Source { get; set; } = "";
}