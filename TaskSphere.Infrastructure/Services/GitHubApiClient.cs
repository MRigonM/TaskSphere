using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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

    public Task<Result<GitHubResponse>> GetAsync(long installationId, string url, CancellationToken cancellationToken = default)
        => SendWithRetryAsync(HttpMethod.Get, installationId, url, body: null, readErrorBody: false, cancellationToken);

    public Task<Result<GitHubResponse>> PostAsync(long installationId, string url, string jsonBody, CancellationToken cancellationToken = default)
        => SendWithRetryAsync(HttpMethod.Post, installationId, url, jsonBody, readErrorBody: true, cancellationToken);

    private async Task<Result<GitHubResponse>> SendWithRetryAsync(
        HttpMethod method,
        long installationId,
        string url,
        string? body,
        bool readErrorBody,
        CancellationToken cancellationToken)
    {
        var attempt = await SendAsync(method, installationId, url, body, cancellationToken);

        // A 401 means the cached token was revoked or expired early. Drop it and retry once;
        // a second 401 is a real failure, not something to loop on.
        if (attempt.StatusCode == HttpStatusCode.Unauthorized)
        {
            attempt.Response?.Dispose();
            _tokenService.Invalidate(installationId);
            attempt = await SendAsync(method, installationId, url, body, cancellationToken);
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
            var description = $"GitHub returned {(int)response.StatusCode} for {url}.";

            if (readErrorBody)
            {
                var message = await ReadGitHubMessageAsync(response, cancellationToken);

                if (message is not null)
                    description += $" {message}";
            }

            return Result<GitHubResponse>.Failure(new Error(CodeFor(response.StatusCode), description));
        }

        var successBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var link = response.Headers.TryGetValues("Link", out var values) ? string.Join(", ", values) : null;

        return Result<GitHubResponse>.Success(new GitHubResponse(successBody, link));
    }

    private async Task<Attempt> SendAsync(
        HttpMethod method,
        long installationId,
        string url,
        string? body,
        CancellationToken cancellationToken)
    {
        var token = await _tokenService.GetInstallationTokenAsync(installationId, cancellationToken);

        if (!token.IsSuccess)
            return new Attempt(null, token.Errors[0], null);

        // Rebuilt per attempt, deliberately: an HttpRequestMessage cannot be resent, so the
        // 401 retry needs the body as a string rather than as HttpContent.
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);

        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, cancellationToken);

        return new Attempt(response, null, response.StatusCode);
    }

    // 401 stays GitHub.RequestFailed: nothing branches on it, and a more specific code would
    // change a contract three sync paths already assert on.
    private static string CodeFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Forbidden => "GitHub.Forbidden",
        HttpStatusCode.NotFound => "GitHub.NotFound",
        HttpStatusCode.UnprocessableEntity => "GitHub.UnprocessableEntity",
        _ => "GitHub.RequestFailed",
    };

    private static async Task<string?> ReadGitHubMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(body))
                return null;

            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("message", out var message))
                return null;

            var text = message.GetString();

            if (string.IsNullOrWhiteSpace(text))
                return null;

            return text.Length <= 200 ? text : text[..200];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsRateLimited(HttpResponseMessage response)
        => response.StatusCode == HttpStatusCode.TooManyRequests
           || (response.StatusCode == HttpStatusCode.Forbidden && response.Headers.RetryAfter is not null);

    private sealed record Attempt(HttpResponseMessage? Response, Error? Failure, HttpStatusCode? StatusCode);
}
