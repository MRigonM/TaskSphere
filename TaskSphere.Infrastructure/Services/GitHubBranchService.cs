using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;

using TaskEntity = TaskSphere.Domain.Entities.Task;

namespace TaskSphere.Infrastructure.Services;

public class GitHubBranchService : IGitHubBranchService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAccessControlService _accessControl;
    private readonly IGitHubApiClient _apiClient;
    private readonly IGitHubTaskLinkResolver _resolver;

    public GitHubBranchService(
        IUnitOfWork unitOfWork,
        IAccessControlService accessControl,
        IGitHubApiClient apiClient,
        IGitHubTaskLinkResolver resolver)
    {
        _unitOfWork = unitOfWork;
        _accessControl = accessControl;
        _apiClient = apiClient;
        _resolver = resolver;
    }

    public async Task<Result<BranchSuggestionDto>> GetSuggestionAsync(
        Guid companyId, string userId, bool isCompanyAdmin, int taskId, CancellationToken cancellationToken = default)
    {
        var context = await LoadAsync(companyId, userId, isCompanyAdmin, taskId, cancellationToken);

        if (!context.IsSuccess)
            return Result<BranchSuggestionDto>.Failure(context.Errors[0]);

        var loaded = context.Value!;
        var key = new TaskKey(loaded.Project.Key, loaded.Task.Number);

        return Result<BranchSuggestionDto>.Success(new BranchSuggestionDto(
            key.ToString(),
            GitHubBranchNameBuilder.Build(key, loaded.Task.Title),
            loaded.Repositories
                .Select(r => new BranchRepositoryOptionDto(r.Id, r.FullName, r.DefaultBranch))
                .ToList()));
    }

    public Task<Result<CreatedBranchDto>> CreateForTaskAsync(
        Guid companyId, string userId, bool isCompanyAdmin, int taskId, CreateBranchDto dto, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Everything both entry points need, in one place: the membership gate, the task, its
    /// project, the installation, and the repositories the project actually links.
    /// </summary>
    private async Task<Result<BranchContext>> LoadAsync(
        Guid companyId, string userId, bool isCompanyAdmin, int taskId, CancellationToken cancellationToken)
    {
        // Before the lookup, deliberately — the same order GitHubTaskActivityService uses.
        // CanAccessTaskAsync is false for a task that does not exist as well as one the caller
        // cannot see, so a non-member cannot tell the two apart.
        if (!isCompanyAdmin && !await _accessControl.CanAccessTaskAsync(companyId, userId, taskId, cancellationToken))
            return Result<BranchContext>.Failure(EntityError.Forbidden);

        var task = await _unitOfWork.Tasks.GetByIdForCompanyAsync(taskId, companyId, cancellationToken);

        if (task is null)
            return Result<BranchContext>.Failure(EntityError.NotFound(taskId));

        if (task.ProjectId is null)
        {
            return Result<BranchContext>.Failure(new Error(
                "GitHub.TaskHasNoProject",
                "This task is not in a project, so it has no key to name a branch with."));
        }

        var project = await _unitOfWork.Projects
            .GetCompanyProjects(companyId)
            .FirstOrDefaultAsync(p => p.Id == task.ProjectId.Value, cancellationToken);

        if (project is null)
            return Result<BranchContext>.Failure(EntityError.NotFound(task.ProjectId.Value));

        var installation = await _unitOfWork.GitHubInstallations.GetByCompanyAsync(companyId, cancellationToken);

        if (installation is null)
        {
            return Result<BranchContext>.Failure(new Error(
                "GitHub.NotConnected",
                "This company is not connected to GitHub."));
        }

        var linkedIds = await _unitOfWork.ProjectRepositoryLinks
            .GetByProject(companyId, project.Id)
            .Select(l => l.GitHubRepositoryId)
            .ToListAsync(cancellationToken);

        var repositories = await _unitOfWork.GitHubRepositories
            .GetByCompany(companyId)
            .Where(r => linkedIds.Contains(r.Id))
            .OrderBy(r => r.FullName)
            .ToListAsync(cancellationToken);

        if (repositories.Count == 0)
        {
            return Result<BranchContext>.Failure(new Error(
                "GitHub.NoLinkedRepository",
                "No GitHub repository is linked to this task's project."));
        }

        return Result<BranchContext>.Success(new BranchContext(task, project, installation, repositories));
    }

    private sealed record BranchContext(
        TaskEntity Task,
        Project Project,
        GitHubInstallation Installation,
        List<GitHubRepository> Repositories);
}
