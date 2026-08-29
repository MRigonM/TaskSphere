using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;
using TaskSphere.Domain.Interfaces;

// The entities, not the namespace: TaskSphere.Domain.Entities.Task shadows Task otherwise.
using GitHubBranch = TaskSphere.Domain.Entities.GitHubBranch;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;

namespace TaskSphere.Infrastructure.Services;

public class GitHubActivitySyncService : IGitHubActivitySyncService
{
    /// <summary>
    /// One number, one meaning — deliberately not configurable. A fixed window is always safe
    /// to re-run, which a per-repository watermark is not: force-push and rebase make "what
    /// changed since last time" genuinely hard to answer correctly.
    /// The window bounds the commits query: only commits after DateTime.UtcNow.AddDays(-SyncWindowDays)
    /// are fetched, and it is applied on every run so the pass is naturally idempotent.
    /// </summary>
    private const int SyncWindowDays = 30;

    private readonly IGitHubApiClient _apiClient;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IGitHubTaskLinkResolver _resolver;
    private readonly IMergeTransitionService _mergeTransitions;
    private readonly GitHubPullRequestMirror _pullRequests;
    private readonly GitHubBranchMirror _branches;
    private readonly GitHubCommitMirror _commits;

    public GitHubActivitySyncService(
        IGitHubApiClient apiClient,
        IUnitOfWork unitOfWork,
        IGitHubTaskLinkResolver resolver,
        IMergeTransitionService mergeTransitions,
        GitHubPullRequestMirror pullRequests,
        GitHubBranchMirror branches,
        GitHubCommitMirror commits)
    {
        _apiClient = apiClient;
        _unitOfWork = unitOfWork;
        _resolver = resolver;
        _mergeTransitions = mergeTransitions;
        _pullRequests = pullRequests;
        _branches = branches;
        _commits = commits;
    }

    public async Task<Result<SyncActivityResultDto>> SyncCompanyAsync(
        Guid companyId,
        string? actorUsername,
        CancellationToken cancellationToken = default)
    {
        var installation = await _unitOfWork.GitHubInstallations.GetByCompanyAsync(companyId, cancellationToken);

        if (installation is null)
        {
            return Result<SyncActivityResultDto>.Failure(new Error(
                "GitHub.NotConnected",
                "This company is not connected to GitHub."));
        }

        var linkedRepositoryIds = await _unitOfWork.ProjectRepositoryLinks
            .GetByCompany(companyId)
            .Select(l => l.GitHubRepositoryId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var repositories = await _unitOfWork.GitHubRepositories
            .GetByCompany(companyId)
            .Where(r => linkedRepositoryIds.Contains(r.Id))
            .OrderBy(r => r.FullName)
            .ToListAsync(cancellationToken);

        var failures = new List<SyncFailureDto>();

        var synced = 0;
        var branchCount = 0;
        var commitCount = 0;
        var pullCount = 0;
        var since = DateTime.UtcNow.AddDays(-SyncWindowDays);

        foreach (var repository in repositories)
        {
            var branchResult = await _branches.RefreshAsync(installation, repository.Id, repository.FullName, cancellationToken);

            if (!branchResult.IsSuccess)
            {
                failures.Add(new SyncFailureDto(repository.FullName, branchResult.Errors[0].Description));
                continue;
            }

            branchCount += branchResult.Value!.Count;

            // A branch that fails is one line in the summary, not the end of the repository:
            // the commits pass reports per branch, so the other branches' commits are still
            // counted and the repository still counts as synced.
            var (inserted, commitFailures) = await _commits.RefreshAsync(
                installation, repository.Id, repository.FullName, branchResult.Value!,
                repository.DefaultBranch, since, cancellationToken);

            failures.AddRange(commitFailures);
            commitCount += inserted;

            // One listing per repository, so this failure is repository-scoped and carries no
            // branch. It still does not un-sync the repository: its branches and the commits
            // that did come back are already recorded.
            var pullResult = await _pullRequests.RefreshAsync(
                installation, repository.Id, repository.FullName, since, cancellationToken);

            if (!pullResult.IsSuccess)
                failures.Add(new SyncFailureDto(repository.FullName, pullResult.Errors[0].Description));
            else
                pullCount += pullResult.Value;

            synced++;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var resolution = await _resolver.ResolveAsync(companyId, cancellationToken);

        // After the pull-request upsert, so State is current. Independent of the resolver: the
        // transition reads head branches, not TaskLink rows.
        var transitions = await _mergeTransitions.ApplyAsync(companyId, actorUsername, null, cancellationToken);

        if (synced > 0)
        {
            installation.ActivitySyncedAtUtc = DateTime.UtcNow;
            await _unitOfWork.GitHubInstallations.Update(installation, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        // Named, because six positional members of which four are ints is a transposition
        // waiting to happen.
        return Result<SyncActivityResultDto>.Success(new SyncActivityResultDto(
            RepositoriesSynced: synced,
            Commits: commitCount,
            Branches: branchCount,
            PullRequests: pullCount,
            LinksCreated: resolution.LinksCreated,
            TasksTransitioned: transitions.Value?.Transitioned ?? 0,
            Failures: failures));
    }
}
