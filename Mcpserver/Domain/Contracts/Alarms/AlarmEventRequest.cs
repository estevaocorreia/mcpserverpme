
namespace Mcpserver.Domain.Contracts.Alarms;

public sealed class AlarmEventRequest
{
    public DateTime Inicio { get; init; }
    public DateTime Fim { get; init; }

    public string? MultipleSources { get; init; }

    public bool ApenasAtivos { get; init; } = false;
}