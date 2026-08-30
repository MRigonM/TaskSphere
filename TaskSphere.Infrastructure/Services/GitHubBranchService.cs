using System.Text.Json;
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

    public async Task<Result<CreatedBranchDto>> CreateForTaskAsync(
        Guid companyId, string userId, bool isCompanyAdmin, int taskId, CreateBranchDto dto, CancellationToken cancellationToken = default)
    {
        var context = await LoadAsync(companyId, userId, isCompanyAdmin, taskId, cancellationToken);

        if (!context.IsSuccess)
            return Result<CreatedBranchDto>.Failure(context.Errors[0]);

        var loaded = context.Value!;

        var repository = ResolveRepository(loaded.Repositories, dto.RepositoryId);

        if (!repository.IsSuccess)
            return Result<CreatedBranchDto>.Failure(repository.Errors[0]);

        var name = (dto.Name ?? "").Trim();
        var key = new TaskKey(loaded.Project.Key, loaded.Task.Number);

        if (!GitHubBranchNameValidator.IsValidRefName(name))
        {
            return Result<CreatedBranchDto>.Failure(new Error(
                "Validation.BranchName",
                "That is not a valid git branch name."));
        }

        if (!GitHubBranchNameValidator.NamesTask(name, key))
        {
            return Result<CreatedBranchDto>.Failure(new Error(
                "Validation.BranchName",
                $"The branch name has to contain {key}, or the branch will never link to this task."));
        }

        var target = repository.Value!;
        var installationId = loaded.Installation.InstallationId;

        var baseSha = await ReadRefShaAsync(installationId, target.FullName, target.DefaultBranch, cancellationToken);

        if (!baseSha.IsSuccess)
        {
            var error = baseSha.Errors[0];

            return Result<CreatedBranchDto>.Failure(error.Code == "GitHub.NotFound"
                ? new Error(
                    "GitHub.DefaultBranchMissing",
                    $"GitHub has no branch '{target.DefaultBranch}' in {target.FullName}. Re-sync the repositories and try again.")
                : error);
        }

        var payload = JsonSerializer.Serialize(new { @ref = $"refs/heads/{name}", sha = baseSha.Value! });

        var created = await _apiClient.PostAsync(
            installationId, $"https://api.github.com/repos/{target.FullName}/git/refs", payload, cancellationToken);

        var headSha = baseSha.Value!;
        var alreadyExisted = false;

        if (!created.IsSuccess)
        {
            var error = created.Errors[0];

            if (error.Code == "GitHub.UnprocessableEntity"
                && error.Description.Contains("Reference already exists", StringComparison.OrdinalIgnoreCase))
            {
                // Not a failure: the branch is there, which is what was asked for. Its head is
                // wherever GitHub has it, not where main is — read it rather than record the
                // base sha as though it were the branch's.
                alreadyExisted = true;

                var existingSha = await ReadRefShaAsync(installationId, target.FullName, name, cancellationToken);

                if (existingSha.IsSuccess)
                    headSha = existingSha.Value!;
            }
            else if (error.Code == "GitHub.Forbidden")
            {
                return Result<CreatedBranchDto>.Failure(new Error(
                    "GitHub.WriteNotApproved",
                    $"TaskSphere cannot write to {target.FullName} yet. A GitHub account owner has to approve the pending 'contents: write' permission request for the installation."));
            }
            else
            {
                return Result<CreatedBranchDto>.Failure(error);
            }
        }

        // IgnoreQueryFilters via the repository's own lookup: IX_GitHubBranches_RepositoryId_Name
        // is unfiltered, so a merged-then-recreated branch has a soft-deleted row that a filtered
        // read would miss and the insert would collide with.
        var branch = await _unitOfWork.GitHubBranches.GetByNameIncludingDeletedAsync(target.Id, name, cancellationToken);

        if (branch is null)
        {
            branch = new GitHubBranch
            {
                GitHubRepositoryId = target.Id,
                CompanyId = companyId,
                Name = name,
                HeadSha = headSha,
            };

            await _unitOfWork.GitHubBranches.AddAsync(branch, cancellationToken);
        }
        else
        {
            branch.CompanyId = companyId;
            branch.HeadSha = headSha;
            branch.IsDeleted = false;
            branch.DeletedAt = null;

            await _unitOfWork.GitHubBranches.Update(branch, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // A pure function of the mirror — no GitHub JSON reaches it, which is the seam B2 rests
        // on. The branch is linked before the caller ever sees a response.
        await _resolver.ResolveAsync(companyId, cancellationToken);

        return Result<CreatedBranchDto>.Success(new CreatedBranchDto(
            branch.Id,
            branch.Name,
            branch.HeadSha,
            $"https://github.com/{target.FullName}/tree/{branch.Name}",
            alreadyExisted));
    }

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

    private static Result<GitHubRepository> ResolveRepository(List<GitHubRepository> linked, int? requestedId)
    {
        if (linked.Count == 1)
            return Result<GitHubRepository>.Success(linked[0]);

        if (requestedId is null)
        {
            return Result<GitHubRepository>.Failure(new Error(
                "GitHub.RepositoryRequired",
                "This project links more than one repository, so the branch needs one chosen."));
        }

        var match = linked.FirstOrDefault(r => r.Id == requestedId.Value);

        // Forbidden, not NotFound: `linked` holds only this project's repositories, so anything
        // else is either another project's or another company's. Same boundary the resolver
        // enforces — a link is what authorizes.
        return match is null
            ? Result<GitHubRepository>.Failure(EntityError.Forbidden)
            : Result<GitHubRepository>.Success(match);
    }

    private async Task<Result<string>> ReadRefShaAsync(
        long installationId, string fullName, string branchName, CancellationToken cancellationToken)
    {
        var response = await _apiClient.GetAsync(
            installationId, $"https://api.github.com/repos/{fullName}/git/ref/heads/{branchName}", cancellationToken);

        if (!response.IsSuccess)
            return Result<string>.Failure(response.Errors[0]);

        try
        {
            using var document = JsonDocument.Parse(response.Value!.Body);

            if (document.RootElement.TryGetProperty("object", out var obj)
                && obj.TryGetProperty("sha", out var sha)
                && sha.GetString() is { Length: > 0 } value)
            {
                return Result<string>.Success(value);
            }
        }
        catch (JsonException)
        {
            // Falls through to the same failure as a well-formed body with no sha.
        }

        return Result<string>.Failure(new Error(
            "GitHub.SyncFailed",
            $"GitHub returned an unreadable ref for {fullName}."));
    }

    private sealed record BranchContext(
        TaskEntity Task,
        Project Project,
        GitHubInstallation Installation,
        List<GitHubRepository> Repositories);
}
