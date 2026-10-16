using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Agent;

namespace TaskSphere.Application.Interfaces;

public interface IAgentService
{
    /// <summary>Creates the owner's agent, or revives it if it was soft-deleted.</summary>
    Task<Result<AgentDto>> CreateForOwnerAsync(string ownerUserId, CancellationToken ct = default);

    Task<Result<AgentStatusDto>> GetMineAsync(string ownerUserId, CancellationToken ct = default);

    /// <summary>Revokes the live token, if any, and issues a new one.</summary>
    Task<Result<IssuedAgentTokenDto>> RegenerateTokenAsync(string ownerUserId, CancellationToken ct = default);

    Task<Result<string>> RevokeTokenAsync(string ownerUserId, CancellationToken ct = default);

    /// <summary>Null for anything that is not a live token of a live agent with a live owner.</summary>
    Task<AgentIdentity?> AuthenticateAsync(string token, CancellationToken ct = default);
}
