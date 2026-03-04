using Mcpserver.Application.Interfaces;
using Mcpserver.Domain.Contracts.Alarms;

namespace Mcpserver.Application.Services;

public sealed class AlarmService
{
    private readonly IAlarmRepository _repo;
    public AlarmService(IAlarmRepository repo) => _repo = repo;

    public Task<AlarmQueryResult> QueryAsync(AlarmQueryRequest request, CancellationToken ct)
        => _repo.QueryAsync(request, ct);

    public Task<IReadOnlyList<string>> ColumnsAsync(CancellationToken ct)
        => _repo.GetColumnsAsync(ct);

    public Task<AlarmEventResult> GetEventsAsync(AlarmEventRequest request, CancellationToken ct)
        => _repo.GetEventsAsync(request, ct);
}
