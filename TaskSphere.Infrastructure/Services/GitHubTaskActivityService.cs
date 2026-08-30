using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Infrastructure.Services;

public class GitHubTaskActivityService : IGitHubTaskActivityService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAccessControlService _accessControl;

    public GitHubTaskActivityService(IUnitOfWork unitOfWork, IAccessControlService accessControl)
    {
        _unitOfWork = unitOfWork;
        _accessControl = accessControl;
    }

    public async Task<Result<TaskGitHubActivityDto>> GetForTaskAsync(
        Guid companyId,
        string userId,
        bool isCompanyAdmin,
        int taskId,
        CancellationToken cancellationToken = default)
    {
        // Before the lookup, deliberately: CanAccessTaskAsync is false for a task that does
        // not exist as well as one the caller cannot see, so a non-member learns nothing.
        if (!isCompanyAdmin && !await _accessControl.CanAccessTaskAsync(companyId, userId, taskId, cancellationToken))
            return Result<TaskGitHubActivityDto>.Failure(EntityError.Forbidden);

        var task = await _unitOfWork.Tasks.GetByIdForCompanyAsync(taskId, companyId, cancellationToken);

        if (task is null)
            return Result<TaskGitHubActivityDto>.Failure(EntityError.NotFound(taskId));

        var installation = await _unitOfWork.GitHubInstallations.GetByCompanyAsync(companyId, cancellationToken);
        var lastSynced = installation?.ActivitySyncedAtUtc;

        var links = await _unitOfWork.TaskLinks
            .GetByTask(companyId, taskId)
            .ToListAsync(cancellationToken);

        if (links.Count == 0 || task.ProjectId is null)
            return Empty(lastSynced);

        // The authorization re-check. A link row survives an unlink, so the read is what
        // decides whether it still grants anything — no cleanup job, no stale grants.
        var authorizedRepositoryIds = (await _unitOfWork.ProjectRepositoryLinks
                .GetByProject(companyId, task.ProjectId.Value)
                .Select(l => l.GitHubRepositoryId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        if (authorizedRepositoryIds.Count == 0)
            return Empty(lastSynced);

        // Filtered, so a soft-deleted repository drops its records the same way a link to one
        // is dropped from the links screen.
        var repositoryNames = await _unitOfWork.GitHubRepositories
            .GetByCompany(companyId)
            .Where(r => authorizedRepositoryIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.FullName, cancellationToken);

        var commitIds = links.Where(l => l.GitHubCommitId is not null).Select(l => l.GitHubCommitId!.Value).ToList();
        var branchIds = links.Where(l => l.GitHubBranchId is not null).Select(l => l.GitHubBranchId!.Value).ToList();
        var pullIds = links.Where(l => l.GitHubPullRequestId is not null).Select(l => l.GitHubPullRequestId!.Value).ToList();

        var commits = await _unitOfWork.GitHubCommits
            .GetByCompany(companyId)
            .Where(c => commitIds.Contains(c.Id))
            .OrderByDescending(c => c.CommittedAtUtc)
            .ToListAsync(cancellationToken);

        // IgnoreQueryFilters, unlike the other two: a branch that GitHub no longer reports is
        // rendered with a marker rather than vanishing from the task's history. No Include on
        // this query — suppression is query-wide.
        var branches = await _unitOfWork.GitHubBranches
            .GetByCompanyIncludingDeleted(companyId)
            .Where(b => branchIds.Contains(b.Id))
            .OrderBy(b => b.Name)
            .ToListAsync(cancellationToken);

        var pulls = await _unitOfWork.GitHubPullRequests
            .GetByCompany(companyId)
            .Where(p => pullIds.Contains(p.Id))
            .OrderByDescending(p => p.OpenedAtUtc)
            .ToListAsync(cancellationToken);

        // Both halves are already in hand: `links` is materialized above, and every via-branch
        // necessarily has its OWN TaskLink on this task — inheritance only flows through a
        // branch already linked — so it is already in `branches`. No extra query.
        var viaBranchIdByCommitId = links
            .Where(l => l.GitHubCommitId is not null && l.ViaGitHubBranchId is not null)
            .ToDictionary(l => l.GitHubCommitId!.Value, l => l.ViaGitHubBranchId!.Value);

        var branchNamesById = branches.ToDictionary(b => b.Id, b => b.Name);

        return Result<TaskGitHubActivityDto>.Success(new TaskGitHubActivityDto(
            commits
                .Where(c => repositoryNames.ContainsKey(c.GitHubRepositoryId))
                .Select(c => new TaskCommitDto(
                    c.Sha,
                    c.Sha.Length >= 7 ? c.Sha[..7] : c.Sha,
                    c.Message,
                    c.AuthorName,
                    c.AuthorLogin,
                    c.CommittedAtUtc,
                    c.HtmlUrl,
                    repositoryNames[c.GitHubRepositoryId],
                    viaBranchIdByCommitId.TryGetValue(c.Id, out var viaId)
                        && branchNamesById.TryGetValue(viaId, out var viaName)
                            ? viaName
                            : null))
                .ToList(),
            branches
                .Where(b => repositoryNames.ContainsKey(b.GitHubRepositoryId))
                .Select(b => new TaskBranchDto(
                    b.Name,
                    b.HeadSha,
                    b.IsDeleted,
                    repositoryNames[b.GitHubRepositoryId]))
                .ToList(),
            pulls
                .Where(p => repositoryNames.ContainsKey(p.GitHubRepositoryId))
                .Select(p => new TaskPullRequestDto(
                    p.Number,
                    p.Title,
                    p.State,
                    p.AuthorLogin,
                    p.OpenedAtUtc,
                    p.MergedAtUtc,
                    p.HtmlUrl,
                    repositoryNames[p.GitHubRepositoryId]))
                .ToList(),
            lastSynced));
    }

    private static Result<TaskGitHubActivityDto> Empty(DateTime? lastSynced)
        => Result<TaskGitHubActivityDto>.Success(new TaskGitHubActivityDto([], [], [], lastSynced));
}
