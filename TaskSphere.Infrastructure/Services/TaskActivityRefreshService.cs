using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
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
            return Result<TaskActivityRefreshDto>.Success(new TaskActivityRefreshDto(false, 0, 0, null));

        var linkedRepositoryIds = await _unitOfWork.ProjectRepositoryLinks
            .GetByProject(companyId, task.ProjectId.Value)
            .Select(l => l.GitHubRepositoryId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (linkedRepositoryIds.Count == 0)
            return Result<TaskActivityRefreshDto>.Success(new TaskActivityRefreshDto(false, 0, 0, null));

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

        if (work.Count == 0)
            return Result<TaskActivityRefreshDto>.Success(new TaskActivityRefreshDto(false, 0, 0, LastStamp(repositories)));

        var since = now.AddDays(-SyncWindowDays);
        var refreshed = 0;

        foreach (var w in work)
        {
            try
            {
                // Branches first and unconditionally: the commits pass consumes the list it
                // returns, and RecordAheadAsync looks up branch rows that must already exist.
                var branchResult = await _branches.RefreshAsync(
                    installation, w.Repository.Id, w.Repository.FullName, cancellationToken);

                if (!branchResult.IsSuccess)
                    continue;

                var didSomething = false;

                if (w.RefreshCommits)
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
                }

                if (!didSomething)
                    continue;

                await _unitOfWork.GitHubRepositories.Update(w.Repository, cancellationToken);

                // Per repository, so a later failure cannot discard earlier work.
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                refreshed++;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A rejected save leaves its entity tracked as Modified; without this the next
                // repository's save re-sends the bad write and fails too.
                _unitOfWork.DiscardPendingChanges();
            }
        }

        // Task 6 adds the resolver and merge-transition tail.
        return Result<TaskActivityRefreshDto>.Success(new TaskActivityRefreshDto(
            Refreshed: refreshed > 0,
            RepositoriesRefreshed: refreshed,
            TasksTransitioned: 0,
            LastSyncedAtUtc: LastStamp(repositories)));
    }

    private static DateTime? LastStamp(List<GitHubRepository> r) => null;
}
