namespace Mcpserver.Domain.Contracts.Meters;

public sealed class MeterListResult
{
    public int Returned { get; set; }
    public List<MeterDetailsDto> Items { get; set; } = new();
    public string Source { get; set; } = "";
}
