using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Infrastructure.Data;

namespace TaskSphere.Infrastructure.Repositories;

public sealed class MemberRepository : GenericRepository<Member, int>, IMemberRepository
{
    private readonly ApplicationDbContext _context;

    public MemberRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public Task<Member?> GetByProjectAndUserIncludingDeletedAsync(int projectId, string userId, CancellationToken ct = default)
        => _context.Members
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, ct);
}