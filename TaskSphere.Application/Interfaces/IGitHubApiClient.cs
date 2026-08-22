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

    /// <summary>
    /// Authenticated POST against the GitHub REST API, using the installation's access token.
    /// Shares the GET path's token handling, one-shot 401 retry and rate-limit typing. Unlike
    /// the GET path, a non-2xx description carries GitHub's own <c>message</c> field: on a
    /// write, "Reference already exists" is the difference between an error and a success.
    /// </summary>
    Task<Result<GitHubResponse>> PostAsync(long installationId, string url, string jsonBody, CancellationToken cancellationToken = default);
}
