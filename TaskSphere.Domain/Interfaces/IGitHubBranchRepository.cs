using TaskSphere.Domain.Entities;

namespace TaskSphere.Domain.Interfaces;

public interface IGitHubBranchRepository : IGenericRepository<GitHubBranch, int>
{
    IQueryable<GitHubBranch> GetByCompany(Guid companyId);
    IQueryable<GitHubBranch> GetByRepository(Guid companyId, int gitHubRepositoryId);

    /// <summary>
    /// Suppresses the soft-delete filter: a branch deleted on GitHub and recreated later must
    /// revive this row rather than collide with the unfiltered unique index.
    /// </summary>
    Task<GitHubBranch?> GetByNameIncludingDeletedAsync(int gitHubRepositoryId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every branch of the company, deleted ones included. The read renders a deleted branch
    /// with a marker rather than dropping it, so it cannot use the filtered query.
    /// </summary>
    IQueryable<GitHubBranch> GetByCompanyIncludingDeleted(Guid companyId);
}
