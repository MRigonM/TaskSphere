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

    public async Task<PagedResult<AuditLog>> GetPagedAsync(Guid companyId, AuditQueryDto query, CancellationToken ct = default)
    {
        var q = _context.AuditLogs.AsNoTracking()
            .Where(a => a.CompanyId == companyId);

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

    public async Task<AuditStatsDto> GetStatsAsync(Guid companyId, int days, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 365);
        var since = DateTimeOffset.UtcNow.AddDays(-days);

        var q = _context.AuditLogs.AsNoTracking()
            .Where(a => a.CompanyId == companyId);

        var total = await q.CountAsync(ct);

        var activeUsers = await q
            .Where(a => a.Username != null)
            .Select(a => a.Username)
            .Distinct()
            .CountAsync(ct);

        var topEndpoints = await q
            .GroupBy(a => a.Action)
            .Select(g => new EndpointStatDto(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToListAsync(ct);

        var dailyCounts = await q
            .Where(a => a.Timestamp >= since)
            .GroupBy(a => a.Timestamp.UtcDateTime.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .OrderBy(x => x.Date)
            .ToListAsync(ct);

        var requestsPerDay = dailyCounts
            .Select(x => new DailyStatDto(DateOnly.FromDateTime(x.Date), x.Count))
            .ToList();

        return new AuditStatsDto(total, activeUsers, topEndpoints, requestsPerDay);
    }
}
