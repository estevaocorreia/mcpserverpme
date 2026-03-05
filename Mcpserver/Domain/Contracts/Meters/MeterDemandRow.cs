namespace Mcpserver.Domain.Contracts.Meters;

public sealed class MeterDemandRow
{
    public string NomeMedidor { get; set; } = "";

    public decimal? Demanda { get; set; }

    public DateTime PeriodoLocal { get; set; }
    public DateTime PeriodoFinalLocal { get; set; }

    public DateTime PeriodoUTC { get; set; }
    public DateTime PeriodoFinalUTC { get; set; }
}