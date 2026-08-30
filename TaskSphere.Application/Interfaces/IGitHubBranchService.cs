using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;

namespace TaskSphere.Application.Interfaces;

/// <summary>
/// Creating a branch is task-scoped, so both members and admins reach it — unlike the activity
/// sync, which is company-wide and stays admin-only. Membership is enforced here rather than by
/// the controller's role gate.
/// </summary>
public interface IGitHubBranchService
{
    Task<Result<BranchSuggestionDto>> GetSuggestionAsync(
        Guid companyId, string userId, bool isCompanyAdmin, int taskId, CancellationToken cancellationToken = default);

    Task<Result<CreatedBranchDto>> CreateForTaskAsync(
        Guid companyId, string userId, bool isCompanyAdmin, int taskId, CreateBranchDto dto, CancellationToken cancellationToken = default);
}
