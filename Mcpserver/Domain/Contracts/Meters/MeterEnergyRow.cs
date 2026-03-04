namespace Mcpserver.Domain.Contracts.Meters;

public sealed class MeterEnergyRow
{
    public string NomeMedidor { get; set; } = "";
    public decimal? ConsumoTotal_kWh { get; set; }
    public DateTime PeriodoLocal { get; set; }
    public DateTime PeriodoFinalLocal { get; set; }
}