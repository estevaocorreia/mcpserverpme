namespace Mcpserver.Domain.Contracts.Meters;

public sealed class MeterListRequest
{
    public string? Search { get; set; }
    public int Take { get; set; } = 200;
    public int Skip { get; set; } = 0;
}
