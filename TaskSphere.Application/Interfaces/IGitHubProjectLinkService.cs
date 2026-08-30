using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;

namespace TaskSphere.Application.Interfaces;

public interface IGitHubProjectLinkService
{
    /// <summary>
    /// Links a project to a repository. Both must belong to the caller's company; a mismatch
    /// answers NotFound rather than Forbidden so the response does not confirm that a
    /// cross-tenant resource exists.
    /// </summary>
    Task<Result<ProjectRepositoryLinkDto>> LinkAsync(Guid companyId, string userId, int projectId, int repositoryId, CancellationToken cancellationToken = default);

    Task<Result> UnlinkAsync(Guid companyId, int projectId, int repositoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A project's linked repositories. Company admins see any project in the company; a
    /// <c>User</c> must be a member, enforced here rather than by the role attribute so the
    /// caller gets a 403 carrying the "Auth.Forbidden" code (§0h).
    /// </summary>
    Task<Result<ProjectRepositoriesDto>> GetProjectRepositoriesAsync(Guid companyId, string userId, bool isCompanyAdmin, int projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every live repository in the company with the projects it is linked to, plus links whose
    /// repository is no longer live reported as per-project counts. Company-wide and admin-only:
    /// the route is gated to the Company role, so there is no membership question to answer.
    /// </summary>
    Task<Result<CompanyRepositoryLinksDto>> GetCompanyLinksAsync(Guid companyId, CancellationToken cancellationToken = default);
}
