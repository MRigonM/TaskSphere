using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;
using TaskSphere.Domain.Interfaces;

// The entity, not the namespace: TaskSphere.Domain.Entities.Task shadows Task otherwise.
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;

namespace TaskSphere.Infrastructure.Services;

public class TaskActivityRefreshService : ITaskActivityRefreshService
{
    /// <summary>
    /// Pull requests keep the project refresh's sixty seconds — and the same column, so a board
    /// load and a tab open share one cooldown rather than each paying for the other.
    /// </summary>
    private static readonly TimeSpan PullCooldown = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Commits cost one listing per branch, so they get their own, much longer window. Sized
    /// against push → alt-tab → open the task, which is slower than merge → look at the board.
    /// </summary>
    private static readonly TimeSpan CommitCooldown = TimeSpan.FromMinutes(5);

    private const int SyncWindowDays = 30;

    private sealed record RepositoryWork(
        GitHubRepository Repository, bool RefreshPulls, bool RefreshCommits);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAccessControlService _accessControl;
    private readonly GitHubBranchMirror _branches;
    private readonly GitHubCommitMirror _commits;
    private readonly GitHubPullRequestMirror _pullRequests;
    private readonly IMergeTransitionService _mergeTransitions;
    private readonly IGitHubTaskLinkResolver _resolver;

    public TaskActivityRefreshService(
        IUnitOfWork unitOfWork,
        IAccessControlService accessControl,
        GitHubBranchMirror branches,
        GitHubCommitMirror commits,
        GitHubPullRequestMirror pullRequests,
        IMergeTransitionService mergeTransitions,
        IGitHubTaskLinkResolver resolver)
    {
        _unitOfWork = unitOfWork;
        _accessControl = accessControl;
        _branches = branches;
        _commits = commits;
        _pullRequests = pullRequests;
        _mergeTransitions = mergeTransitions;
        _resolver = resolver;
    }

    public async Task<Result<TaskActivityRefreshDto>> RefreshAsync(
        Guid companyId,
        int taskId,
        string userId,
        bool isCompanyAdmin,
        string? actorUsername,
        CancellationToken cancellationToken = default)
    {
        // Before the lookup, deliberately — the same order GitHubTaskActivityService.GetForTaskAsync
        // uses, so a non-member cannot distinguish a missing task from a forbidden one.
        if (!isCompanyAdmin && !await _accessControl.CanAccessTaskAsync(companyId, userId, taskId, cancellationToken))
            return Result<TaskActivityRefreshDto>.Failure(EntityError.Forbidden);

        var task = await _unitOfWork.Tasks.GetByIdForCompanyAsync(taskId, companyId, cancellationToken);

        if (task is null)
            return Result<TaskActivityRefreshDto>.Failure(EntityError.NotFound(taskId));

        if (task.ProjectId is null)
            return Result<TaskActivityRefreshDto>.Success(Quiet(null, null, null));

        var linkedRepositoryIds = await _unitOfWork.ProjectRepositoryLinks
            .GetByProject(companyId, task.ProjectId.Value)
            .Select(l => l.GitHubRepositoryId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (linkedRepositoryIds.Count == 0)
            return Result<TaskActivityRefreshDto>.Success(Quiet(null, null, null));

        var installation = await _unitOfWork.GitHubInstallations.GetByCompanyAsync(companyId, cancellationToken);

        if (installation is null)
        {
            return Result<TaskActivityRefreshDto>.Failure(new Error(
                "GitHub.NotConnected",
                "This company is not connected to GitHub."));
        }

        var now = DateTime.UtcNow;
        var pullCutoff = now - PullCooldown;
        var commitCutoff = now - CommitCooldown;

        var repositories = await _unitOfWork.GitHubRepositories
            .GetByCompany(companyId)
            .Where(r => linkedRepositoryIds.Contains(r.Id))
            .OrderBy(r => r.FullName)
            .ToListAsync(cancellationToken);

        var work = repositories
            .Select(r => new RepositoryWork(
                r,
                RefreshPulls: r.PullRequestsRefreshedAtUtc == null || r.PullRequestsRefreshedAtUtc < pullCutoff,
                RefreshCommits: r.CommitsRefreshedAtUtc == null || r.CommitsRefreshedAtUtc < commitCutoff))
            .Where(w => w.RefreshPulls || w.RefreshCommits)
            .ToList();

        // Every repository sat inside both cooldowns. Neither pass was attempted, and that is a
        // healthy skip rather than a failure — the distinction the old single bool could not make.
        if (work.Count == 0)
        {
            return Result<TaskActivityRefreshDto>.Success(Quiet(
                LastStamp(repositories),
                OldestStamp(repositories, r => r.CommitsRefreshedAtUtc),
                OldestStamp(repositories, r => r.PullRequestsRefreshedAtUtc)));
        }

        var since = now.AddDays(-SyncWindowDays);
        var refreshed = 0;

        // Reported per pass, because the two carry different cooldowns and fail independently:
        // a repository GitHub is failing on must not read as a healthy cooldown skip.
        var failures = new List<SyncFailureDto>();
        var commitsAttempted = work.Any(w => w.RefreshCommits);
        var pullsAttempted = work.Any(w => w.RefreshPulls);
        var commitsFailed = false;
        var pullsFailed = false;

        foreach (var w in work)
        {
            try
            {
                // Branches first when they run at all: the commits pass consumes the list they
                // return, and RecordAheadAsync looks up branch rows that must already exist. But
                // a branch failure does not stop pull-request processing: the transition's input
                // (merged PRs) must flow even if the Activity tab (branches) is temporarily
                // unavailable. Branches exist to show work history; pull requests drive task
                // completion. (Same rationale as ProjectActivityRefreshService's ordering.)
                var branchResult = await _branches.RefreshAsync(
                    installation, w.Repository.Id, w.Repository.FullName, cancellationToken);

                var didSomething = false;

                if (!branchResult.IsSuccess)
                {
                    failures.Add(new SyncFailureDto(w.Repository.FullName, branchResult.Errors[0].Description));

                    // Branches gate the commits pass, so a failed listing means commits could not
                    // run at all for this repository. Counting it against commits is what stops a
                    // blocked pass reading as a successful one.
                    if (w.RefreshCommits)
                        commitsFailed = true;
                }

                if (branchResult.IsSuccess && w.RefreshCommits)
                {
                    var (_, commitFailures) = await _commits.RefreshAsync(
                        installation, w.Repository.Id, w.Repository.FullName, branchResult.Value!,
                        w.Repository.DefaultBranch, since, cancellationToken);

                    // Any per-branch failure withholds the stamp: a partial pass must not buy
                    // five minutes of silence for the branches that did not come back.
                    if (commitFailures.Count == 0)
                    {
                        w.Repository.CommitsRefreshedAtUtc = DateTime.UtcNow;
                        didSomething = true;
                    }
                    else
                    {
                        // The mirror has always returned these; this path used to drop them on the
                        // floor, which is exactly why a failing repository was indistinguishable
                        // from a healthy cooldown skip.
                        failures.AddRange(commitFailures);
                        commitsFailed = true;
                    }
                }

                if (w.RefreshPulls)
                {
                    var pullResult = await _pullRequests.RefreshAsync(
                        installation, w.Repository.Id, w.Repository.FullName, since, cancellationToken);

                    if (pullResult.IsSuccess)
                    {
                        w.Repository.PullRequestsRefreshedAtUtc = DateTime.UtcNow;
                        didSomething = true;
                    }
                    else
                    {
                        failures.Add(new SyncFailureDto(w.Repository.FullName, pullResult.Errors[0].Description));
                        pullsFailed = true;
                    }
                }

                if (!didSomething)
                    continue;

                await _unitOfWork.GitHubRepositories.Update(w.Repository, cancellationToken);

                // Per repository, so a later failure cannot discard earlier work.
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                refreshed++;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // A rejected save leaves its entity tracked as Modified; without this the next
                // repository's save re-sends the bad write and fails too.
                _unitOfWork.DiscardPendingChanges();

                // Previously swallowed with no trace at all, so a repository that threw answered
                // exactly like one that was quietly inside its cooldown. Both passes are marked
                // because the throw can come from either and there is no telling which after the
                // fact — over-reporting a failure is the safe direction.
                failures.Add(new SyncFailureDto(w.Repository.FullName, ex.Message));

                if (w.RefreshCommits)
                    commitsFailed = true;

                if (w.RefreshPulls)
                    pullsFailed = true;
            }
        }

        // Skipped entirely when nothing was refreshed: the resolver reads every commit, branch and
        // pull request in the company, and a cooldown hit must stay free.
        if (refreshed > 0)
            await _resolver.ResolveAsync(companyId, cancellationToken);

        // Scoped to this task's repositories, not company-wide: opening one task must not transition
        // tasks on unrelated projects.
        var transitions = await _mergeTransitions.ApplyAsync(
            companyId, actorUsername, linkedRepositoryIds, cancellationToken);

        // Named, because a positional record of two bools and a date per pass is a transposition
        // waiting to happen.
        return Result<TaskActivityRefreshDto>.Success(new TaskActivityRefreshDto(
            Refreshed: refreshed > 0,
            RepositoriesRefreshed: refreshed,
            TasksTransitioned: transitions.Value?.Transitioned ?? 0,
            LastSyncedAtUtc: LastStamp(repositories),
            Commits: new RefreshPassDto(
                Attempted: commitsAttempted,
                Succeeded: commitsAttempted && !commitsFailed,
                RefreshedAtUtc: OldestStamp(repositories, r => r.CommitsRefreshedAtUtc)),
            PullRequests: new RefreshPassDto(
                Attempted: pullsAttempted,
                Succeeded: pullsAttempted && !pullsFailed,
                RefreshedAtUtc: OldestStamp(repositories, r => r.PullRequestsRefreshedAtUtc)),
            Failures: failures));
    }

    /// <summary>
    /// The OLDEST of the per-repository stamps, deliberately: the panel may only claim freshness
    /// as of the least recently refreshed repository it is showing. Max() would let one
    /// just-refreshed repository vouch for a stale one beside it. Null when any repository has
    /// never been refreshed at all — "unknown", not "old".
    ///
    /// The same rule applies one level down, within a single repository: a repository's own
    /// stamp is the OLDER of its two passes (via <see cref="Older"/>), not the newer one — a
    /// repository whose pull-request pass failed must not have its just-refreshed commits stamp
    /// vouch for a pull-request list that was never fetched.
    ///
    /// Shared with GitHubTaskActivityService.GetForTaskAsync, which computes the read side's
    /// "Last synced" label from the same rule over the same repository stamp columns — the two
    /// must never disagree.
    /// </summary>
    /// <summary>
    /// Nothing was fetched, and nothing failed: no project, no linked repositories, or every
    /// repository inside both cooldowns. Stamps are whatever the columns already held, so the
    /// panel can still say when each pass last succeeded.
    /// </summary>
    private static TaskActivityRefreshDto Quiet(
        DateTime? lastSynced, DateTime? commitsStamp, DateTime? pullsStamp) =>
        new(
            Refreshed: false,
            RepositoriesRefreshed: 0,
            TasksTransitioned: 0,
            LastSyncedAtUtc: lastSynced,
            Commits: new RefreshPassDto(Attempted: false, Succeeded: false, RefreshedAtUtc: commitsStamp),
            PullRequests: new RefreshPassDto(Attempted: false, Succeeded: false, RefreshedAtUtc: pullsStamp),
            Failures: []);

    /// <summary>
    /// One pass's stamp across the task's repositories, under the same pessimistic rule as
    /// <see cref="LastStamp"/>: null if any repository has never had that pass succeed, because
    /// the panel may only claim freshness it can claim for all of them. Null is "unknown", not
    /// "old" — which is also what a failed pass leaves behind, since it never writes its column.
    /// </summary>
    internal static DateTime? OldestStamp(
        List<GitHubRepository> repositories, Func<GitHubRepository, DateTime?> select)
    {
        var stamps = repositories.Select(select).ToList();

        return stamps.Count == 0 || stamps.Any(s => s is null) ? null : stamps.Min();
    }

    internal static DateTime? LastStamp(List<GitHubRepository> repositories)
    {
        var stamps = repositories
            .Select(r => Older(r.CommitsRefreshedAtUtc, r.PullRequestsRefreshedAtUtc))
            .ToList();

        return stamps.Count == 0 || stamps.Any(s => s is null) ? null : stamps.Min();
    }

    /// <summary>Null if either input is null — "unknown", not "old" — otherwise the earlier one.</summary>
    private static DateTime? Older(DateTime? a, DateTime? b) =>
        a is null || b is null ? null : (a < b ? a : b);
}
