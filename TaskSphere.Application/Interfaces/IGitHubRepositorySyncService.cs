using TaskSphere.Domain.Common;
using TaskSphere.Domain.Entities;

namespace TaskSphere.Application.Interfaces;

public interface IGitHubRepositorySyncService
{
    /// <summary>
    /// Pulls the installation's repositories from GitHub and reconciles them into the database:
    /// repositories are matched on GitHub's numeric id (never <c>FullName</c>, which changes on
    /// rename), rows absent from the response are soft-deleted, and the installation's
    /// repository selection is refreshed. Runs on the install callback and on demand.
    /// <para>
    /// Returns the number of live repositories after the sync.
    /// </para>
    /// </summary>
    Task<Result<int>> SyncAsync(GitHubInstallation installation, CancellationToken cancellationToken = default);
}
