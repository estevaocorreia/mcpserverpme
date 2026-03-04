namespace Mcpserver.Domain.Contracts.Meters;

public sealed class MeterDemandRequest
{
    public string Inicio { get; set; } = "";
    public string Fim { get; set; } = "";

    public List<string> DisplayNames { get; set; } = new();

    public int Take { get; set; } = 1000;
}