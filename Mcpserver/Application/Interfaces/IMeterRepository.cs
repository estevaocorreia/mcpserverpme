using Mcpserver.Domain.Contracts.Meters;
using Mcpserver.Models;

namespace Mcpserver.Application.Interfaces;

public interface IMeterRepository
{
    Task<MeterListResult> ListAsync(MeterListRequest request, CancellationToken ct);


    Task<MeterEnergyResult> Meter_EnergyAsync(MeterEnergyQuery query, CancellationToken ct);
    Task<MeterDemandResult> Meter_DemandAsync(MeterDemandQuery query, CancellationToken ct);
    Task<MeterEventsByClassificationResult> Meter_EventsByClassificationAsync(Mcpserver.Models.MeterEventsByClassificationQuery query, CancellationToken ct);

}
