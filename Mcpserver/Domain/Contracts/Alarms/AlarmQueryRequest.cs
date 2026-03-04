namespace Mcpserver.Domain.Contracts.Alarms;

public sealed class AlarmQueryRequest
{
   
    public int Take { get; init; } = 100;
    public int Skip { get; init; } = 0;

  
    public string? OrderBy { get; init; }
    public bool Desc { get; init; } = true;

    
    public string? DateColumn { get; init; }         
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }

    public Dictionary<string, object?> EqualFilters { get; init; } = new();


    public string? ContainsText { get; init; }
    public string[]? TextColumns { get; init; }      
}
