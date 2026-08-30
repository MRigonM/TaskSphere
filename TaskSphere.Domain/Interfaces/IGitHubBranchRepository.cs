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
    ///
    /// Unlike the other IncludingDeleted lookups, this one returns a composable IQueryable
    /// instead of terminating with FirstOrDefaultAsync: filter suppression is query-wide, so
    /// chaining .Include(b => b.Repository) on the result would also materialize soft-deleted
    /// repositories.
    /// </summary>
    IQueryable<GitHubBranch> GetByCompanyIncludingDeleted(Guid companyId);
}
