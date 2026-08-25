using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Infrastructure.Services;

public class ProjectActivityRefreshService : IProjectActivityRefreshService
{
    /// <summary>
    /// Per repository, not per project: one repository can be linked to several projects, and
    /// refreshing it once serves every board that shows it. Sized against merge → alt-tab →
    /// look at the board, which is often under a minute — a longer window would leave people
    /// reaching for the Sync button, which is the behaviour this exists to remove.
    /// </summary>
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The same window the full sync uses, so a pull request is visible to both paths or
    /// neither.
    /// </summary>
    private const int SyncWindowDays = 30;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAccessControlService _accessControl;
    private readonly GitHubPullRequestMirror _pullRequests;
    private readonly IMergeTransitionService _mergeTransitions;

    public ProjectActivityRefreshService(
        IUnitOfWork unitOfWork,
        IAccessControlService accessControl,
        GitHubPullRequestMirror pullRequests,
        IMergeTransitionService mergeTransitions)
    {
        _unitOfWork = unitOfWork;
        _accessControl = accessControl;
        _pullRequests = pullRequests;
        _mergeTransitions = mergeTransitions;
    }

    public async Task<Result<ProjectActivityRefreshDto>> RefreshAsync(
        Guid companyId,
        int projectId,
        string userId,
        bool isCompanyAdmin,
        string? actorUsername,
        CancellationToken cancellationToken = default)
    {
        var project = await _unitOfWork.Projects
            .GetCompanyProjects(companyId)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project is null)
            return Result<ProjectActivityRefreshDto>.Failure("Project not found.");

        if (!isCompanyAdmin &&
            !await _accessControl.CanAccessProjectAsync(companyId, userId, projectId, cancellationToken))
        {
            return Result<ProjectActivityRefreshDto>.Failure(
                new Error("Auth.Forbidden", "You are not a member of this project."));
        }

        // Before the installation lookup on purpose: a project that cannot transition anything
        // costs nothing, and a company with no GitHub connection at all stays quiet.
        if (!project.AutoDoneOnMerge)
            return Result<ProjectActivityRefreshDto>.Success(new ProjectActivityRefreshDto(false, 0, 0));

        var installation = await _unitOfWork.GitHubInstallations
            .GetByCompanyAsync(companyId, cancellationToken);

        if (installation is null)
        {
            return Result<ProjectActivityRefreshDto>.Failure(new Error(
                "GitHub.NotConnected",
                "This company is not connected to GitHub."));
        }

        var linkedRepositoryIds = await _unitOfWork.ProjectRepositoryLinks
            .GetByProject(companyId, projectId)
            .Select(l => l.GitHubRepositoryId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (linkedRepositoryIds.Count == 0)
            return Result<ProjectActivityRefreshDto>.Success(new ProjectActivityRefreshDto(false, 0, 0));

        var cutoff = DateTime.UtcNow - Cooldown;

        var repositories = await _unitOfWork.GitHubRepositories
            .GetByCompany(companyId)
            .Where(r => linkedRepositoryIds.Contains(r.Id))
            .Where(r => r.PullRequestsRefreshedAtUtc == null || r.PullRequestsRefreshedAtUtc < cutoff)
            .OrderBy(r => r.FullName)
            .ToListAsync(cancellationToken);

        if (repositories.Count == 0)
            return Result<ProjectActivityRefreshDto>.Success(new ProjectActivityRefreshDto(false, 0, 0));

        var since = DateTime.UtcNow.AddDays(-SyncWindowDays);
        var refreshed = 0;

        foreach (var repository in repositories)
        {
            try
            {
                var result = await _pullRequests.RefreshAsync(
                    installation, repository.Id, repository.FullName, since, cancellationToken);

                // A repository that failed keeps its old stamp, so the next board load retries
                // it rather than waiting out a cooldown it never earned.
                if (!result.IsSuccess)
                    continue;

                repository.PullRequestsRefreshedAtUtc = DateTime.UtcNow;
                await _unitOfWork.GitHubRepositories.Update(repository, cancellationToken);

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

        var transitions = await _mergeTransitions.ApplyAsync(
            companyId, actorUsername, linkedRepositoryIds, cancellationToken);

        return Result<ProjectActivityRefreshDto>.Success(new ProjectActivityRefreshDto(
            Refreshed: refreshed > 0,
            RepositoriesRefreshed: refreshed,
            TasksTransitioned: transitions.Value?.Transitioned ?? 0));
    }
}
