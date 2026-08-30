using TaskSphere.Domain.Common;

namespace TaskSphere.Application.Interfaces;

public interface IGitHubTokenService
{
    /// <summary>
    /// Returns a cached or freshly exchanged installation access token.
    /// The installation id must always be derived server-side from the authenticated
    /// CompanyId — never accepted from request input.
    /// </summary>
    Task<Result<string>> GetInstallationTokenAsync(long installationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops a cached token. Called when GitHub rejects it, so the next call re-exchanges.
    /// </summary>
    void Invalidate(long installationId);
}
