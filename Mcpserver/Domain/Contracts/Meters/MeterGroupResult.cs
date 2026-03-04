namespace Mcpserver.Domain.Contracts.Meters;

public sealed class MeterGroupResult
{
    public int Groups { get; set; }
    public int Returned { get; set; }
    public Dictionary<string, List<MeterDetailsDto>> ItemsByGroup { get; set; } = new();
    public string Source { get; set; } = "";
}
