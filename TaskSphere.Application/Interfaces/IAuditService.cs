using TaskSphere.Domain.DataTransferObjects.Audit;

namespace TaskSphere.Application.Interfaces;

public interface IAuditService
{
    Task<PagedResult<AuditLogDto>> GetPagedAsync(Guid companyId, AuditQueryDto query, CancellationToken ct = default);
    Task<AuditStatsDto> GetStatsAsync(Guid companyId, int days, CancellationToken ct = default);
}
