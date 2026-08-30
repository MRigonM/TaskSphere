using TaskSphere.Domain.Entities;

namespace TaskSphere.Domain.Interfaces;

public interface IGitHubRepositoryRepository : IGenericRepository<GitHubRepository, int>
{
    IQueryable<GitHubRepository> GetByCompany(Guid companyId);

    /// <summary>
    /// Same contract as <see cref="IGitHubInstallationRepository.GetByGitHubIdIncludingDeletedAsync"/>:
    /// IX_GitHubRepositories_RepositoryId is unfiltered, so the lookup must be too.
    /// Bare entity, no navigations.
    /// </summary>
    Task<GitHubRepository?> GetByGitHubIdIncludingDeletedAsync(long repositoryId, CancellationToken cancellationToken = default);
}
