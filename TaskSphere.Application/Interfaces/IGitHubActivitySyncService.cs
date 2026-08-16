using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;

namespace TaskSphere.Application.Interfaces;

/// <summary>
/// Pulls commits, branches and pull requests for every repository with at least one live
/// project link, upserts them into the mirror on GitHub's natural keys, then runs the
/// resolver. Admin-triggered rather than automatic: the cost is 2 + B calls per repository,
/// B being the branch count.
/// </summary>
public interface IGitHubActivitySyncService
{
    Task<Result<SyncActivityResultDto>> SyncCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);
}
