namespace Mcpserver.Domain.Contracts.Auth;

public class AdAuthRequest
{
    public string TeamsToken { get; set; } = string.Empty;
}

public class AdAuthResult
{
    public bool Authenticated { get; set; }
    public string? Error { get; set; }
    public AdUserDto? User { get; set; }
    public string Source { get; set; } = string.Empty;
}

public class AdUserDto
{
    public string? Username { get; set; }
    public string? Upn { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? Department { get; set; }
    public string? Title { get; set; }
    public bool Enabled { get; set; }
    public List<string> Groups { get; set; } = [];
}