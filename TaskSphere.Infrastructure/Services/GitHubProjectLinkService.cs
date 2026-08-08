using AutoMapper;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;
using TaskSphere.Domain.Interfaces;

// The entities, not the namespace — TaskSphere.Domain.Entities.Task shadows Task otherwise.
using ProjectRepositoryLink = TaskSphere.Domain.Entities.ProjectRepositoryLink;

namespace TaskSphere.Infrastructure.Services;

public class GitHubProjectLinkService : IGitHubProjectLinkService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAccessControlService _accessControl;
    private readonly IMapper _mapper;

    public GitHubProjectLinkService(
        IUnitOfWork unitOfWork,
        IAccessControlService accessControl,
        IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _accessControl = accessControl;
        _mapper = mapper;
    }

    public async Task<Result<ProjectRepositoryLinkDto>> LinkAsync(
        Guid companyId,
        string userId,
        int projectId,
        int repositoryId,
        CancellationToken cancellationToken = default)
    {
        // Both sides checked against the caller's company before anything is written. A
        // cross-tenant link is the entire reason this entity carries CompanyId.
        if (!await _unitOfWork.Projects.CompanyOwnsProjectAsync(companyId, projectId, cancellationToken))
            return Result<ProjectRepositoryLinkDto>.Failure(EntityError.NotFound(projectId));

        var repository = await _unitOfWork.GitHubRepositories
            .GetByCompany(companyId)
            .FirstOrDefaultAsync(r => r.Id == repositoryId, cancellationToken);

        if (repository is null)
            return Result<ProjectRepositoryLinkDto>.Failure(EntityError.NotFound(repositoryId));

        var existing = await _unitOfWork.ProjectRepositoryLinks
            .GetLinkAsync(companyId, projectId, repositoryId, cancellationToken);

        // Idempotent rather than 409: linking something already linked is not an error worth
        // making the client handle.
        if (existing is not null)
        {
            existing.Repository = repository;
            return Result<ProjectRepositoryLinkDto>.Success(_mapper.Map<ProjectRepositoryLinkDto>(existing));
        }

        var link = new ProjectRepositoryLink
        {
            ProjectId = projectId,
            GitHubRepositoryId = repositoryId,
            CompanyId = companyId,
            LinkedByUserId = userId,
        };

        await _unitOfWork.ProjectRepositoryLinks.AddAsync(link, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        link.Repository = repository;

        return Result<ProjectRepositoryLinkDto>.Success(_mapper.Map<ProjectRepositoryLinkDto>(link));
    }

    public async Task<Result> UnlinkAsync(
        Guid companyId,
        int projectId,
        int repositoryId,
        CancellationToken cancellationToken = default)
    {
        var link = await _unitOfWork.ProjectRepositoryLinks
            .GetLinkAsync(companyId, projectId, repositoryId, cancellationToken);

        if (link is null)
            return Result.Failure(EntityError.NotFound(repositoryId));

        // Soft: the filtered unique index is what lets the pair be linked again afterwards.
        link.IsDeleted = true;
        link.DeletedAt = DateTime.UtcNow;

        await _unitOfWork.ProjectRepositoryLinks.Update(link, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<ProjectRepositoriesDto>> GetProjectRepositoriesAsync(
        Guid companyId,
        string userId,
        bool isCompanyAdmin,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (!await _unitOfWork.Projects.CompanyOwnsProjectAsync(companyId, projectId, cancellationToken))
            return Result<ProjectRepositoriesDto>.Failure(EntityError.NotFound(projectId));

        // Membership is enforced here, not by the role attribute, so the response carries the
        // "Auth.Forbidden" code rather than being a bare framework 403 (§0h).
        if (!isCompanyAdmin && !await _accessControl.CanAccessProjectAsync(companyId, userId, projectId, cancellationToken))
            return Result<ProjectRepositoriesDto>.Failure(EntityError.Forbidden);

        // Deliberately NOT .Include(l => l.Repository). GitHubRepository carries a query filter
        // and is the *required* end of this relationship, so EF renders the Include as an INNER
        // JOIN: a link whose repository is soft-deleted disappears from the result entirely
        // rather than coming back with a null navigation. That silently makes the count zero
        // and hides the very rows it is supposed to report.
        var links = await _unitOfWork.ProjectRepositoryLinks
            .GetByProject(companyId, projectId)
            .ToListAsync(cancellationToken);

        if (links.Count == 0)
            return Result<ProjectRepositoriesDto>.Success(new ProjectRepositoriesDto([], 0));

        var linkedIds = links.Select(l => l.GitHubRepositoryId).ToList();

        // Filtered, so this returns only live repositories — no IgnoreQueryFilters anywhere on
        // the read path.
        var repositories = await _unitOfWork.GitHubRepositories
            .GetByCompany(companyId)
            .Where(r => linkedIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        var live = new List<ProjectRepositoryLink>();

        foreach (var link in links)
        {
            if (!repositories.TryGetValue(link.GitHubRepositoryId, out var repository))
                continue;

            // Assigned explicitly rather than left to change-tracker fixup: the flattened
            // FullName must not depend on what else happens to be loaded in this context.
            link.Repository = repository;
            live.Add(link);
        }

        // Option A: links to a dead repository are counted, never rendered. The link row itself
        // survives untouched.
        return Result<ProjectRepositoriesDto>.Success(new ProjectRepositoriesDto(
            _mapper.Map<List<ProjectRepositoryLinkDto>>(live),
            links.Count - live.Count));
    }
}
