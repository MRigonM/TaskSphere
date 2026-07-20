using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.DataTransferObjects.Audit;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Infrastructure.Data;

namespace TaskSphere.Infrastructure.Repositories;

public class AuditRepository : IAuditRepository
{
    private readonly ApplicationDbContext _context;

    public AuditRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<AuditLog>> GetPagedAsync(AuditQueryDto query, CancellationToken ct = default)
    {
        var q = _context.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Username))
            q = q.Where(a => a.Username != null && a.Username.Contains(query.Username));

        if (!string.IsNullOrWhiteSpace(query.Action))
            q = q.Where(a => a.Action.Contains(query.Action));

        if (!string.IsNullOrWhiteSpace(query.HttpMethod))
            q = q.Where(a => a.HttpMethod == query.HttpMethod.ToUpper());

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(a => a.Timestamp)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<AuditLog>(items, total, query.Page, query.PageSize);
    }
}
