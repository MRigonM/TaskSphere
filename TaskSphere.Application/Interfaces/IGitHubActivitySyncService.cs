using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;

namespace TaskSphere.Application.Interfaces;

/// <summary>
/// Pulls activity for every repository with at least one live project link, upserts it into
/// the mirror on GitHub's natural keys, then runs the resolver. Branches and commits are synced;
/// pull requests are unbuilt. This costs 1 + B calls per repository (B being the branch count),
/// which is why this is admin-triggered, not automatic.
/// </summary>
public interface IGitHubActivitySyncService
{
    Task<Result<SyncActivityResultDto>> SyncCompanyAsync(
        Guid companyId,
        string? actorUsername,
        CancellationToken cancellationToken = default);
}
