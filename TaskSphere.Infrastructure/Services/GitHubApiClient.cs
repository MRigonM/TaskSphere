using System.Net;
using System.Net.Http.Headers;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;

namespace TaskSphere.Infrastructure.Services;

public class GitHubApiClient : IGitHubApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IGitHubTokenService _tokenService;

    public GitHubApiClient(HttpClient httpClient, IGitHubTokenService tokenService)
    {
        _httpClient = httpClient;
        _tokenService = tokenService;
    }

    public async Task<Result<GitHubResponse>> GetAsync(long installationId, string url, CancellationToken cancellationToken = default)
    {
        var attempt = await SendAsync(installationId, url, cancellationToken);

        // A 401 means the cached token was revoked or expired early. Drop it and retry once;
        // a second 401 is a real failure, not something to loop on.
        if (attempt.StatusCode == HttpStatusCode.Unauthorized)
        {
            attempt.Response?.Dispose();
            _tokenService.Invalidate(installationId);
            attempt = await SendAsync(installationId, url, cancellationToken);
        }

        if (attempt.Failure is not null)
            return Result<GitHubResponse>.Failure(attempt.Failure);

        using var response = attempt.Response!;

        if (IsRateLimited(response))
        {
            var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds
                             ?? response.Headers.RetryAfter?.Date?.Subtract(DateTimeOffset.UtcNow).TotalSeconds;

            return Result<GitHubResponse>.Failure(new Error(
                "GitHub.RateLimited",
                retryAfter is not null
                    ? $"GitHub rate limit hit. Retry after {Math.Ceiling(retryAfter.Value)}s."
                    : "GitHub rate limit hit."));
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result<GitHubResponse>.Failure(new Error(
                "GitHub.RequestFailed",
                $"GitHub returned {(int)response.StatusCode} for {url}."));
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var link = response.Headers.TryGetValues("Link", out var values) ? string.Join(", ", values) : null;

        return Result<GitHubResponse>.Success(new GitHubResponse(body, link));
    }

    private async Task<Attempt> SendAsync(long installationId, string url, CancellationToken cancellationToken)
    {
        var token = await _tokenService.GetInstallationTokenAsync(installationId, cancellationToken);

        if (!token.IsSuccess)
            return new Attempt(null, token.Errors[0], null);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);

        var response = await _httpClient.SendAsync(request, cancellationToken);

        return new Attempt(response, null, response.StatusCode);
    }

    private static bool IsRateLimited(HttpResponseMessage response)
        => response.StatusCode == HttpStatusCode.TooManyRequests
           || (response.StatusCode == HttpStatusCode.Forbidden && response.Headers.RetryAfter is not null);

    private sealed record Attempt(HttpResponseMessage? Response, Error? Failure, HttpStatusCode? StatusCode);
}
