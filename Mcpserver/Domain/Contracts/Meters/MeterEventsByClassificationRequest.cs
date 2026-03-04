namespace Mcpserver.Domain.Contracts.Meters;

public sealed class MeterEventsByClassificationRequest
{
    
    public string InicioUtc { get; set; } = "";
    public string? FimUtc { get; set; } = null;

    public List<string>? DisplayNames { get; set; }

    public List<string>? Classifications { get; set; }

    public int Take { get; set; } = 5000;
}