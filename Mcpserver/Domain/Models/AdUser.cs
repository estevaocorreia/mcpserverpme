using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mcpserver.Domain.Models
{
    public record AdUser
 {
    public string? Username { get; init; }
    public string? Upn { get; init; }
    public string? DisplayName { get; init; }
    public string? Email { get; init; }
    public string? Department { get; init; }
    public string? Title { get; init; }
    public bool Enabled { get; init; }
    public List<string> Groups { get; init; } = new();
}
}
