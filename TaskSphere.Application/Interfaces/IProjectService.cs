using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Project;

namespace TaskSphere.Application.Interfaces;

public interface IProjectService
{
    Task<Result<ProjectDto>> CreateAsync(Guid companyId, CreateProjectDto dto, CancellationToken ct = default);
    Task<Result<IEnumerable<ProjectDto>>> GetAllAsync(Guid companyId, string userId, bool isCompanyAdmin, CancellationToken ct = default);
    Task<Result<ProjectDto>> GetByIdAsync(Guid companyId, int projectId, string userId, bool isCompanyAdmin, CancellationToken ct = default);
    Task<Result<IEnumerable<MemberDto>>> GetMembersAsync(Guid companyId, int projectId, string userId, bool isCompanyAdmin, CancellationToken ct = default);
    Task<Result<string>> AddMemberAsync(Guid companyId, int projectId, string userId, CancellationToken ct = default);
    Task<Result<string>> RemoveMemberAsync(Guid companyId, int projectId, string userId, CancellationToken ct = default);
    Task<Result<IEnumerable<ProjectDto>>> GetMembersProjects(Guid companyId, string userId, CancellationToken ct = default);
}
