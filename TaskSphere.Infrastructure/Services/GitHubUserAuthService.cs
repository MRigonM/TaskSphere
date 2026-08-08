using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Infrastructure.Configuration;

namespace TaskSphere.Infrastructure.Services;

/// <summary>
/// Runs on its own <see cref="HttpClient"/> with no auth handler and no BaseAddress (§0p): the
/// two calls sit on different hosts, and the credential is a user token that must never travel
/// on the installation-token client.
/// </summary>
public class GitHubUserAuthService : IGitHubUserAuthService
{
    private const string TokenExchangeUrl = "https://github.com/login/oauth/access_token";
    private const string UserInstallationsUrl = "https://api.github.com/user/installations?per_page=100";

    // A user with more installations than this is not a case worth looping forever over; the
    // callback fails closed and the user retries.
    private const int MaxPages = 20;

    private readonly HttpClient _httpClient;
    private readonly GitHubAppOptions _options;

    public GitHubUserAuthService(HttpClient httpClient, IOptions<GitHubAppOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<Result<string>> ExchangeCodeForUserTokenAsync(string code, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenExchangeUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["code"] = code,
            }),
        };

        // GitHub answers form-encoded unless asked otherwise, which parses as no token at all.
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return Failure<string>($"GitHub returned {(int)response.StatusCode} exchanging the authorization code.");

        var payload = await response.Content.ReadFromJsonAsync<TokenExchangeResponse>(cancellationToken);

        // GitHub reports a rejected code as 200 with an error body, so the status code alone
        // would let a stale or forged code through.
        if (payload is null || !string.IsNullOrEmpty(payload.Error))
        {
            return Failure<string>(payload?.ErrorDescription
                                   ?? "GitHub rejected the authorization code.");
        }

        if (string.IsNullOrEmpty(payload.AccessToken))
            return Failure<string>("GitHub returned no access token for the authorization code.");

        return Result<string>.Success(payload.AccessToken);
    }

    public async Task<Result<bool>> UserHasInstallationAsync(string userToken, long installationId, CancellationToken cancellationToken = default)
    {
        var found = await FindUserInstallationAsync(userToken, installationId, cancellationToken);

        return found.IsSuccess
            ? Result<bool>.Success(found.Value is not null)
            : Result<bool>.Failure(found.Errors[0]);
    }

    public async Task<Result<GitHubUserInstallation?>> FindUserInstallationAsync(string userToken, long installationId, CancellationToken cancellationToken = default)
    {
        var url = UserInstallationsUrl;

        for (var page = 0; page < MaxPages && url is not null; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return Failure<GitHubUserInstallation?>($"GitHub returned {(int)response.StatusCode} listing the user's installations.");

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            UserInstallationsResponse? payload;

            try
            {
                payload = JsonSerializer.Deserialize<UserInstallationsResponse>(body);
            }
            catch (JsonException)
            {
                return Failure<GitHubUserInstallation?>("GitHub returned an unreadable installations response.");
            }

            if (payload?.Installations is null)
                return Failure<GitHubUserInstallation?>("GitHub returned no installations list.");

            var match = payload.Installations.FirstOrDefault(i => i.Id == installationId);

            if (match is not null)
            {
                return Result<GitHubUserInstallation?>.Success(new GitHubUserInstallation(
                    match.Id,
                    match.Account?.Login ?? "",
                    match.Account?.Type ?? "",
                    match.RepositorySelection ?? ""));
            }

            url = NextPageUrl(response);
        }

        return Result<GitHubUserInstallation?>.Success(null);
    }

    /// <summary>
    /// GitHub paginates with the Link header: <c>&lt;url&gt;; rel="next"</c>.
    /// </summary>
    private static string? NextPageUrl(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var values))
            return null;

        foreach (var segment in string.Join(",", values).Split(','))
        {
            if (!segment.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase))
                continue;

            var start = segment.IndexOf('<');
            var end = segment.IndexOf('>');

            if (start >= 0 && end > start)
                return segment[(start + 1)..end];
        }

        return null;
    }

    // One error code for both calls: the caller turns any failure here into the same 403, and
    // telling an attacker which step rejected them is free information.
    private static Result<T> Failure<T>(string message)
        => Result<T>.Failure(new Error("GitHub.UserAuthFailed", message));

    private sealed record TokenExchangeResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);

    private sealed record UserInstallationsResponse(
        [property: JsonPropertyName("installations")] List<UserInstallation>? Installations);

    private sealed record UserInstallation(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("account")] InstallationAccount? Account,
        [property: JsonPropertyName("repository_selection")] string? RepositorySelection);

    private sealed record InstallationAccount(
        [property: JsonPropertyName("login")] string? Login,
        [property: JsonPropertyName("type")] string? Type);
}
