using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Infrastructure.Services;

public class TaskActivityRefreshService : ITaskActivityRefreshService
{
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

        // Task 4 filters by cooldown; Task 5 fills in the passes.
        return Result<TaskActivityRefreshDto>.Success(new TaskActivityRefreshDto(false, 0, 0, null));
    }
}
