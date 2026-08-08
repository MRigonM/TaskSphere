using TaskSphere.Domain.Entities;

namespace TaskSphere.Domain.Interfaces;

public interface IGitHubInstallationRepository : IGenericRepository<GitHubInstallation, int>
{
    Task<GitHubInstallation?> GetByCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up by GitHub's installation id with soft-delete filtering disabled, because
    /// IX_GitHubInstallations_InstallationId is unfiltered — a filtered lookup would miss a
    /// disconnected row and then collide on insert. Returns the bare entity and loads no
    /// navigations: IgnoreQueryFilters is query-wide, so an Include here would also
    /// materialize soft-deleted related rows.
    /// </summary>
    Task<GitHubInstallation?> GetByGitHubIdIncludingDeletedAsync(long installationId, CancellationToken cancellationToken = default);
}
