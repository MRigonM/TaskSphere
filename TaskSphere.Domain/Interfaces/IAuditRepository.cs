using TaskSphere.Domain.DataTransferObjects.Audit;
using TaskSphere.Domain.Entities;

namespace TaskSphere.Domain.Interfaces;

public interface IAuditRepository
{
    Task<PagedResult<AuditLog>> GetPagedAsync(Guid companyId, AuditQueryDto query, CancellationToken ct = default);
    Task<AuditStatsDto> GetStatsAsync(Guid companyId, int days, CancellationToken ct = default);
}
