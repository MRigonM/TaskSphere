using AutoMapper;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.DataTransferObjects.Audit;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Application.Services;

public class AuditService : IAuditService
{
    private readonly IReadOnlyUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public AuditService(IReadOnlyUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<PagedResult<AuditLogDto>> GetPagedAsync(AuditQueryDto query, CancellationToken ct = default)
    {
        var paged = await _unitOfWork.AuditLogs.GetPagedAsync(query, ct);
        var dtos = _mapper.Map<List<AuditLogDto>>(paged.Items);
        return new PagedResult<AuditLogDto>(dtos, paged.Total, paged.Page, paged.PageSize);
    }
}
