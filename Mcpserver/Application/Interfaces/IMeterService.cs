using Mcpserver.Domain.Contracts.Meters;

namespace Mcpserver.Application.Interfaces;

public interface IMeterService
{
    Task<MeterListResult> ListAsync(MeterListRequest request, CancellationToken ct);

    Task<MeterEnergyResult> Meter_EnergyAsync(MeterEnergyRequest request, CancellationToken ct);
    Task<MeterDemandResult> Meter_DemandAsync(MeterDemandRequest request, CancellationToken ct);

    Task<MeterEventsByClassificationResult> Meter_EventsByClassificationAsync(MeterEventsByClassificationRequest request, CancellationToken ct);

    Task<MeterNetworkStatusResult> Meter_NetworkStatusAsync(MeterNetworkStatusRequest request, CancellationToken ct);

}
