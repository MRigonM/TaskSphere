using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;

namespace TaskSphere.Application.Interfaces;

public interface IGitHubTaskActivityService
{
    /// <summary>
    /// A task's GitHub activity, read entirely from the mirror. The repo↔project link is
    /// re-checked on every call, so unlinking a repository hides its activity immediately.
    /// The access check precedes the task lookup, so a non-member cannot tell a missing task
    /// from a forbidden one.
    /// </summary>
    Task<Result<TaskGitHubActivityDto>> GetForTaskAsync(
        Guid companyId,
        string userId,
        bool isCompanyAdmin,
        int taskId,
        CancellationToken cancellationToken = default);
}
