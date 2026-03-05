namespace Mcpserver.Domain.Contracts.Meters;

public sealed class MeterNetworkStatusRow
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Address { get; set; } = "";
    public string Site { get; set; } = "";
    public string SiteStatus { get; set; } = "";

    public string EnabledText { get; set; } = "";

    public string Protocol { get; set; } = "";
    public string Description { get; set; } = "";

    public bool IsEnabled { get; set; }
    public bool IsConnected { get; set; }
}