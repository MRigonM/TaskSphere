using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Infrastructure.Data;
using Task = System.Threading.Tasks.Task;

namespace TaskSphere.Infrastructure.Repositories;

public class AgentTokenRepository : IAgentTokenRepository
{
    private readonly ApplicationDbContext _context;

    public AgentTokenRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(AgentToken token, CancellationToken ct = default)
        => await _context.AgentTokens.AddAsync(token, ct);

    public Task<AgentToken?> GetLiveForAgentAsync(string agentUserId, CancellationToken ct = default)
        => _context.AgentTokens.FirstOrDefaultAsync(
            t => t.AgentUserId == agentUserId && t.RevokedAtUtc == null, ct);

    public Task<AgentToken?> FindLiveByHashAsync(byte[] tokenHash, CancellationToken ct = default)
        => _context.AgentTokens
            .Include(t => t.Agent)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.RevokedAtUtc == null, ct);
}
