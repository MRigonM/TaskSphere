using TaskSphere.Domain.Common;

namespace TaskSphere.Application.Interfaces;

/// <summary>
/// Body plus the raw Link header, which is how GitHub paginates. Task 15 follows it.
/// </summary>
public sealed record GitHubResponse(string Body, string? LinkHeader);

public interface IGitHubApiClient
{
    /// <summary>
    /// Authenticated GET against the GitHub REST API, using the installation's access token.
    /// Rate limiting surfaces as a typed failure (code "GitHub.RateLimited") rather than an
    /// exception; acting on Retry-After is sub-project D's job.
    /// </summary>
    Task<Result<GitHubResponse>> GetAsync(long installationId, string url, CancellationToken cancellationToken = default);
}
