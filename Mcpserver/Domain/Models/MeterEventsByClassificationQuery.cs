namespace Mcpserver.Models;

public sealed class MeterEventsByClassificationQuery
{
    public DateTime InicioUtc { get; set; }
    public DateTime? FimUtc { get; set; }

    public List<string>? DisplayNames { get; set; }
    public List<string>? Classifications { get; set; }

    public int Take { get; set; } = 5000;
}