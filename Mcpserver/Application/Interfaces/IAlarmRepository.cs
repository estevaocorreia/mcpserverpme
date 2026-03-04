using Mcpserver.Domain.Contracts.Alarms;


namespace Mcpserver.Application.Interfaces;

public interface IAlarmRepository
{
    Task<AlarmQueryResult> QueryAsync(AlarmQueryRequest request, CancellationToken ct);
    Task<IReadOnlyList<string>> GetColumnsAsync(CancellationToken ct);
    Task<AlarmEventResult> GetEventsAsync(AlarmEventRequest request, CancellationToken ct);
}