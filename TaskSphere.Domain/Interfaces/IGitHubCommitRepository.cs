using TaskSphere.Domain.Entities;

namespace TaskSphere.Domain.Interfaces;

public interface IGitHubCommitRepository : IGenericRepository<GitHubCommit, int>
{
    IQueryable<GitHubCommit> GetByCompany(Guid companyId);
    IQueryable<GitHubCommit> GetByRepository(Guid companyId, int gitHubRepositoryId);

    /// <summary>
    /// Suppresses the soft-delete filter, because IX_GitHubCommits_RepositoryId_Sha is
    /// unfiltered. Returns a bare entity with no navigations loaded — filter suppression is
    /// query-wide, so an Include here would materialize soft-deleted related rows too.
    /// </summary>
    Task<GitHubCommit?> GetByShaIncludingDeletedAsync(int gitHubRepositoryId, string sha, CancellationToken cancellationToken = default);
}
