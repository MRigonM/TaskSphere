using TaskSphere.Domain.Entities;

namespace TaskSphere.Domain.Interfaces;

public interface IGitHubBranchCommitRepository : IGenericRepository<GitHubBranchCommit, int>
{
    IQueryable<GitHubBranchCommit> GetByCompany(Guid companyId);

    /// <summary>
    /// The upsert guard for the sync's per-commit write. Named ForPair rather than overloading
    /// ExistsAsync, whose single-argument form takes a primary key — two ints in either order
    /// is a transposition waiting to happen.
    /// <para>
    /// No IgnoreQueryFilters, unlike the mirror lookups: the unique index is filtered on
    /// IsDeleted, so a soft-deleted row neither blocks a new one nor needs reviving.
    /// </para>
    /// </summary>
    Task<bool> ExistsForPairAsync(int gitHubBranchId, int gitHubCommitId, CancellationToken cancellationToken = default);
}
