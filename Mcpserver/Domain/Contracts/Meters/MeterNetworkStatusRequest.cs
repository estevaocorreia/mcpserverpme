namespace Mcpserver.Domain.Contracts.Meters;

public sealed class MeterNetworkStatusRequest
{
    public string? Search { get; set; }
    public string? Site { get; set; }
    public bool? Enabled { get; set; }

    public bool? Connected { get; set; }

    public string? SiteStatusContains { get; set; }

    public int Take { get; set; } = 1000;
}