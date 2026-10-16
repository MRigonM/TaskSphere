using TaskSphere.Domain.Entities;
using Task = System.Threading.Tasks.Task;

namespace TaskSphere.Domain.Interfaces;

/// <summary>
/// Not an IGenericRepository: that is constrained to BaseEntity, and AgentToken is a plain class
/// (the IAuditRepository pattern). Queries are tracked, because every caller writes back.
/// </summary>
public interface IAgentTokenRepository
{
    Task AddAsync(AgentToken token, CancellationToken ct = default);
    Task<AgentToken?> GetLiveForAgentAsync(string agentUserId, CancellationToken ct = default);

    /// <summary>The live token with this hash, with its Agent loaded.</summary>
    Task<AgentToken?> FindLiveByHashAsync(byte[] tokenHash, CancellationToken ct = default);
}
