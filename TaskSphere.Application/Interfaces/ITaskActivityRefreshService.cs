using TaskSphere.Domain.Common;

namespace TaskSphere.Application.Interfaces;

/// <summary>
/// <c>Refreshed</c> is false when nothing was fetched — the task has no project, its project
/// links no repositories, or every repository was inside both cooldowns. All ordinary
/// outcomes, not errors. <c>LastSyncedAtUtc</c> is derived from the task's own repositories
/// after the run, so the panel's label matches what it is showing.
/// </summary>
public sealed record TaskActivityRefreshDto(
    bool Refreshed,
    int RepositoriesRefreshed,
    int TasksTransitioned,
    DateTime? LastSyncedAtUtc);

/// <summary>
/// Refreshes branches, commits and pull requests for the repositories linked to one task's
/// project, then runs the resolver and the merge → Done transition scoped to those
/// repositories. Fired by opening a task's Activity tab, so it is reachable by project members
/// and not only company admins — task access is what authorizes it, the same fact the activity
/// read relies on.
/// <para>
/// Unlike the project refresh this includes commits, because the Activity tab is mostly
/// commits. That costs one listing per branch, which is why the commits pass carries its own
/// five-minute cooldown while pull requests keep their sixty-second one.
/// </para>
/// </summary>
public interface ITaskActivityRefreshService
{
    Task<Result<TaskActivityRefreshDto>> RefreshAsync(
        Guid companyId,
        int taskId,
        string userId,
        bool isCompanyAdmin,
        string? actorUsername,
        CancellationToken cancellationToken = default);
}
