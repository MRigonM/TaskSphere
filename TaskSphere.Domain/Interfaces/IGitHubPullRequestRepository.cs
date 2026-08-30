using TaskSphere.Domain.Entities;

namespace TaskSphere.Domain.Interfaces;

public interface IGitHubPullRequestRepository : IGenericRepository<GitHubPullRequest, int>
{
    IQueryable<GitHubPullRequest> GetByCompany(Guid companyId);
    IQueryable<GitHubPullRequest> GetByRepository(Guid companyId, int gitHubRepositoryId);

    /// <summary>Suppresses the soft-delete filter; the number index is unfiltered.</summary>
    Task<GitHubPullRequest?> GetByNumberIncludingDeletedAsync(int gitHubRepositoryId, int number, CancellationToken cancellationToken = default);
}
