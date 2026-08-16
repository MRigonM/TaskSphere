using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;

namespace TaskSphere.Application.Interfaces;

/// <summary>
/// Pulls activity for every repository with at least one live project link, upserts it into
/// the mirror on GitHub's natural keys, then runs the resolver. Branches only so far; commits
/// and pull requests join the same pass, which is what takes the cost to 2 + B calls per
/// repository (B being the branch count) and is why this is admin-triggered, not automatic.
/// </summary>
public interface IGitHubActivitySyncService
{
    Task<Result<SyncActivityResultDto>> SyncCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);
}
