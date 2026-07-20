using TaskSphere.Domain.DataTransferObjects.Audit;

namespace TaskSphere.Application.Interfaces;

public interface IAuditService
{
    Task<PagedResult<AuditLogDto>> GetPagedAsync(AuditQueryDto query, CancellationToken ct = default);
}
