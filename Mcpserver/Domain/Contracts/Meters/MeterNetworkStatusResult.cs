namespace Mcpserver.Domain.Contracts.Meters;

public sealed class MeterNetworkStatusResult
{
    public int Returned { get; set; }
    public List<MeterNetworkStatusRow> Items { get; set; } = new();
    public string Source { get; set; } = "";
}