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
/// Refreshes pull requests for one project's linked repositories, then runs the merge → Done
/// transition scoped to those repositories. Triggered by opening a board or a backlog, so it is
/// reachable by project members and not only company admins — the repository↔project link is
/// what authorizes it, the same fact create-branch-from-task relies on.
/// <para>
/// Pull requests only. Commits and branches cost 1 + B calls per repository and the transition
/// reads neither, so this stays affordable enough to run on a page load.
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
