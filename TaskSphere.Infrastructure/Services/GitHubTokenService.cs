using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;

namespace TaskSphere.Infrastructure.Services;

public class GitHubTokenService : IGitHubTokenService
{
    // Evict early so a token never expires mid-request.
    private static readonly TimeSpan ExpiryHeadroom = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly IGitHubAppJwtProvider _jwtProvider;
    private readonly IMemoryCache _cache;

    public GitHubTokenService(HttpClient httpClient, IGitHubAppJwtProvider jwtProvider, IMemoryCache cache)
    {
        _httpClient = httpClient;
        _jwtProvider = jwtProvider;
        _cache = cache;
    }

    // Keyed by installation id, never by anything derived from request input: two tenants
    // must never collide on a cache entry.
    private static string CacheKey(long installationId) => $"github:installation-token:{installationId}";

    public async Task<Result<string>> GetInstallationTokenAsync(long installationId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue<string>(CacheKey(installationId), out var cached) && !string.IsNullOrEmpty(cached))
            return Result<string>.Success(cached);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.github.com/app/installations/{installationId}/access_tokens");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwtProvider.CreateJwt());

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<string>.Failure(new Error(
                "GitHub.TokenExchangeFailed",
                $"GitHub returned {(int)response.StatusCode} exchanging an installation token."));
        }

        var payload = await response.Content.ReadFromJsonAsync<AccessTokenResponse>(cancellationToken);

        if (payload is null || string.IsNullOrEmpty(payload.Token))
        {
            return Result<string>.Failure(new Error(
                "GitHub.TokenExchangeFailed",
                "GitHub returned no token in the access_tokens response."));
        }

        var lifetime = payload.ExpiresAt - DateTimeOffset.UtcNow - ExpiryHeadroom;

        if (lifetime > TimeSpan.Zero)
            _cache.Set(CacheKey(installationId), payload.Token, lifetime);

        return Result<string>.Success(payload.Token);
    }

    public void Invalidate(long installationId) => _cache.Remove(CacheKey(installationId));

    private sealed record AccessTokenResponse(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);
}
