using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;

namespace TaskSphere.Application.Interfaces;

/// <summary>
/// The outcome of one pass — commits or pull requests — across the task's repositories.
/// <para>
/// The three states this type exists to keep apart, because a single <c>bool</c> could not:
/// <list type="bullet">
/// <item><c>Attempted: false</c> — every repository was inside this pass's cooldown. A healthy
/// skip, not a failure, and it costs no GitHub call.</item>
/// <item><c>Attempted: true, Succeeded: true</c> — the pass ran and every repository answered.
/// Fetching nothing new is still success.</item>
/// <item><c>Attempted: true, Succeeded: false</c> — at least one repository failed. Which ones,
/// and why, is in <see cref="TaskActivityRefreshDto.Failures"/>.</item>
/// </list>
/// </para>
/// <c>RefreshedAtUtc</c> is the oldest stamp across the task's repositories after the run, the
/// same pessimistic rule <c>LastStamp</c> applies: null means "unknown", never "old". A pass
/// that failed leaves it as it was rather than claiming freshness it did not earn.
/// </summary>
public sealed record RefreshPassDto(
    bool Attempted,
    bool Succeeded,
    DateTime? RefreshedAtUtc);

/// <summary>
/// <c>Refreshed</c> is false when nothing was fetched — the task has no project, its project
/// links no repositories, or every repository was inside both cooldowns. All ordinary
/// outcomes, not errors. <c>LastSyncedAtUtc</c> is derived from the task's own repositories
/// after the run, so the panel's label matches what it is showing.
/// <para>
/// <c>Refreshed</c> is a convenience roll-up and <b>cannot</b> distinguish "fetched", "skipped
/// by cooldown" and "failed" — reading it alone is what produced a wrong diagnosis on
/// 2026-08-30. Read <see cref="Commits"/>, <see cref="PullRequests"/> and <see cref="Failures"/>
/// for the actual outcome; each pass reports separately because they carry different cooldowns
/// and fail independently.
/// </para>
/// <c>Failures</c> is empty on a clean run and on a pure cooldown skip. Partial success is a
/// result, not an error: the endpoint still answers 200 carrying them, the same contract the
/// company-wide sync uses.
/// </summary>
public sealed record TaskActivityRefreshDto(
    bool Refreshed,
    int RepositoriesRefreshed,
    int TasksTransitioned,
    DateTime? LastSyncedAtUtc,
    RefreshPassDto Commits,
    RefreshPassDto PullRequests,
    IReadOnlyList<SyncFailureDto> Failures);

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
