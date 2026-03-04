namespace Mcpserver.Models;

public sealed class MeterDemandQuery
{
    public DateTime Inicio { get; set; }
    public DateTime Fim { get; set; }
    public List<string> DisplayNames { get; set; } = new();
    public int Take { get; set; } = 1000;
}