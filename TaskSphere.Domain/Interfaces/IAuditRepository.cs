using TaskSphere.Domain.DataTransferObjects.Audit;
using TaskSphere.Domain.Entities;

namespace TaskSphere.Domain.Interfaces;

public interface IAuditRepository
{
    Task<PagedResult<AuditLog>> GetPagedAsync(AuditQueryDto query, CancellationToken ct = default);
}
