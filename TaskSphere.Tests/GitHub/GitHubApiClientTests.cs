using System.Net;
using System.Net.Http.Headers;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Infrastructure.Services;

namespace TaskSphere.Tests.GitHub;

public class GitHubApiClientTests
{
    /// <summary>
    /// Tier 2 per the plan's test strategy: the client under test gets a fake token service,
    /// never a fake of its own interface.
    /// </summary>
    private sealed class StubTokenService : IGitHubTokenService
    {
        private readonly Queue<string> _tokens;

        public StubTokenService(params string[] tokens) => _tokens = new Queue<string>(tokens);

        public int InvalidateCount { get; private set; }

        public Task<Result<string>> GetInstallationTokenAsync(long installationId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<string>.Success(_tokens.Count > 0 ? _tokens.Dequeue() : "ghs_default"));

        public void Invalidate(long installationId) => InvalidateCount++;
    }

    private sealed class FailingTokenService : IGitHubTokenService
    {
        public Task<Result<string>> GetInstallationTokenAsync(long installationId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<string>.Failure(new Error("GitHub.TokenExchangeFailed", "nope")));

        public void Invalidate(long installationId) { }
    }

    private static GitHubApiClient NewClient(FakeHttpMessageHandler handler, IGitHubTokenService tokenService)
        => new(new HttpClient(handler), tokenService);

    [Fact]
    public async Task AttachesTheInstallationTokenAsABearerCredential()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(HttpStatusCode.OK, "[]");
        var client = NewClient(handler, new StubTokenService("ghs_installation"));

        var result = await client.GetAsync(42, "https://api.github.com/installation/repositories");

        Assert.True(result.IsSuccess);
        Assert.Equal("ghs_installation", handler.AuthorizationHeaders[0]);
    }

    [Fact]
    public async Task ReturnsBodyAndLinkHeader_SoPaginationCanBeFollowed()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(
            HttpStatusCode.OK,
            """{"total_count":1}""",
            response => response.Headers.Add("Link", "<https://api.github.com/installation/repositories?page=2>; rel=\"next\""));

        var client = NewClient(handler, new StubTokenService("ghs_a"));

        var result = await client.GetAsync(42, "https://api.github.com/installation/repositories");

        Assert.True(result.IsSuccess);
        Assert.Contains("total_count", result.Value!.Body, StringComparison.Ordinal);
        Assert.Contains("rel=\"next\"", result.Value.LinkHeader!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unauthorized_InvalidatesTheCachedTokenAndRetriesExactlyOnce()
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(HttpStatusCode.Unauthorized)
            .Enqueue(HttpStatusCode.OK, "[]");

        var tokenService = new StubTokenService("ghs_stale", "ghs_fresh");
        var client = NewClient(handler, tokenService);

        var result = await client.GetAsync(42, "https://api.github.com/installation/repositories");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.CallCount);
        Assert.Equal(1, tokenService.InvalidateCount);
        Assert.Equal("ghs_stale", handler.AuthorizationHeaders[0]);
        Assert.Equal("ghs_fresh", handler.AuthorizationHeaders[1]);
    }

    [Fact]
    public async Task TwoConsecutiveUnauthorized_Fails_WithoutLooping()
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(HttpStatusCode.Unauthorized)
            .Enqueue(HttpStatusCode.Unauthorized);

        var client = NewClient(handler, new StubTokenService("a", "b"));

        var result = await client.GetAsync(42, "https://api.github.com/installation/repositories");

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.RequestFailed", result.Errors[0].Code);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task RateLimiting_MapsToATypedFailure_NotAnException()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(
            HttpStatusCode.TooManyRequests,
            configure: response => response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60)));

        var client = NewClient(handler, new StubTokenService("ghs_a"));

        var result = await client.GetAsync(42, "https://api.github.com/installation/repositories");

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.RateLimited", result.Errors[0].Code);
        Assert.Contains("60", result.Errors[0].Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecondaryRateLimit_ReturnedAs403WithRetryAfter_IsAlsoTreatedAsRateLimiting()
    {
        // GitHub signals secondary rate limits with 403 + Retry-After, not 429. Without this
        // branch it would surface as a generic request failure and read like a permissions bug.
        var handler = new FakeHttpMessageHandler().Enqueue(
            HttpStatusCode.Forbidden,
            configure: response => response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)));

        var client = NewClient(handler, new StubTokenService("ghs_a"));

        var result = await client.GetAsync(42, "https://api.github.com/installation/repositories");

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.RateLimited", result.Errors[0].Code);
    }

    [Fact]
    public async Task TokenFailure_ShortCircuits_WithoutCallingGitHub()
    {
        var handler = new FakeHttpMessageHandler();
        var client = NewClient(handler, new FailingTokenService());

        var result = await client.GetAsync(42, "https://api.github.com/installation/repositories");

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.TokenExchangeFailed", result.Errors[0].Code);
        Assert.Equal(0, handler.CallCount);
    }
}
