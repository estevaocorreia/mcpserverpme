using Mcpserver.Domain.Contracts.Auth;

namespace Mcpserver.Application.Interfaces;

public interface IAdAuthService
{
    Task<AdAuthResult> AuthenticateAsync(AdAuthRequest req, CancellationToken ct);
}