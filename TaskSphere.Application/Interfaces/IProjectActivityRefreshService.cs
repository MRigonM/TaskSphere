using TaskSphere.Domain.Common;

namespace TaskSphere.Application.Interfaces;

/// <summary>
/// <c>Refreshed</c> is false when nothing was fetched — the project opted out, or every
/// repository was inside its cooldown. Both are ordinary outcomes, not errors.
/// </summary>
public sealed record ProjectActivityRefreshDto(
    bool Refreshed,
    int RepositoriesRefreshed,
    int TasksTransitioned);

/// <summary>
/// Refreshes branches and pull requests for one project's linked repositories, then runs the
/// merge → Done transition scoped to those repositories. Triggered by opening a board or a
/// backlog, so it is reachable by project members and not only company admins — the
/// repository↔project link is what authorizes it, the same fact create-branch-from-task relies on.
/// <para>
/// Two calls per repository — the pull-request listing and the branch listing — while commits
/// remain Sync-only because they cost one listing per branch. The transition reads only head
/// branches, not TaskLink rows. The branch pass and the resolver exist so the task's Activity
/// tab shows the work, not to feed the transition.
/// </para>
/// </summary>
public interface IProjectActivityRefreshService
{
    Task<Result<ProjectActivityRefreshDto>> RefreshAsync(
        Guid companyId,
        int projectId,
        string userId,
        bool isCompanyAdmin,
        string? actorUsername,
        CancellationToken cancellationToken = default);
}
