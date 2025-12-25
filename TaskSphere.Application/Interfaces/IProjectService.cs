using TaskSphere.Application.DataTransferObjects.Project;
using TaskSphere.Domain.Common;

namespace TaskSphere.Application.Interfaces;

public interface IProjectService
{
    Task<Result<ProjectDto>> CreateAsync(Guid companyId, CreateProjectDto dto, CancellationToken ct = default);
    Task<Result<IEnumerable<ProjectDto>>> GetAllAsync(Guid companyId, CancellationToken ct = default);

    Task<Result<IEnumerable<MemberDto>>> GetMembersAsync(Guid companyId, int projectId, CancellationToken ct = default);
    Task<Result<string>> AddMemberAsync(Guid companyId, int projectId, string userId, CancellationToken ct = default);
    Task<Result<string>> RemoveMemberAsync(Guid companyId, int projectId, string userId, CancellationToken ct = default);
}