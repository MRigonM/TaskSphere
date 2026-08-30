using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TaskSphere.Application.Interfaces;
using TaskSphere.Infrastructure.Services;

namespace TaskSphere.Tests.GitHub;

public class GitHubTokenServiceTests
{
    private static string TokenJson(string token, DateTimeOffset expiresAt)
        => $$"""{"token":"{{token}}","expires_at":"{{expiresAt.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}}"}""";

    private static (GitHubTokenService Service, FakeHttpMessageHandler Handler, IMemoryCache Cache) NewService(
        FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var jwtProvider = new GitHubAppJwtProvider(
            Options.Create(GitHubAppOptionsAndJwtTests.ValidOptions()));
        var cache = new MemoryCache(new MemoryCacheOptions());

        return (new GitHubTokenService(httpClient, jwtProvider, cache), handler, cache);
    }

    [Fact]
    public async Task ExchangesAndReturnsAToken()
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(HttpStatusCode.Created, TokenJson("ghs_abc", DateTimeOffset.UtcNow.AddHours(1)));

        var (service, _, _) = NewService(handler);

        var result = await service.GetInstallationTokenAsync(42);

        Assert.True(result.IsSuccess);
        Assert.Equal("ghs_abc", result.Value);
        Assert.Equal(1, handler.CallCount);

        // The App JWT, not an installation token, authenticates the exchange itself.
        Assert.NotNull(handler.AuthorizationHeaders[0]);
        Assert.Contains("/app/installations/42/access_tokens", handler.Requests[0].RequestUri!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecondCallWithinTheWindow_MakesNoHttpRequest()
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(HttpStatusCode.Created, TokenJson("ghs_abc", DateTimeOffset.UtcNow.AddHours(1)));

        var (service, _, _) = NewService(handler);

        await service.GetInstallationTokenAsync(42);
        var second = await service.GetInstallationTokenAsync(42);

        Assert.True(second.IsSuccess);
        Assert.Equal("ghs_abc", second.Value);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task TokenExpiringInsideTheHeadroom_IsNotCached()
    {
        // expires_at is 2 minutes out, inside the 5-minute headroom, so caching it would hand
        // out a token that dies mid-request.
        var handler = new FakeHttpMessageHandler()
            .Enqueue(HttpStatusCode.Created, TokenJson("ghs_short", DateTimeOffset.UtcNow.AddMinutes(2)))
            .Enqueue(HttpStatusCode.Created, TokenJson("ghs_short2", DateTimeOffset.UtcNow.AddMinutes(2)));

        var (service, _, _) = NewService(handler);

        await service.GetInstallationTokenAsync(42);
        await service.GetInstallationTokenAsync(42);

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task DifferentInstallationIds_NeverShareACacheEntry()
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(HttpStatusCode.Created, TokenJson("ghs_tenant_a", DateTimeOffset.UtcNow.AddHours(1)))
            .Enqueue(HttpStatusCode.Created, TokenJson("ghs_tenant_b", DateTimeOffset.UtcNow.AddHours(1)));

        var (service, _, _) = NewService(handler);

        var a = await service.GetInstallationTokenAsync(1);
        var b = await service.GetInstallationTokenAsync(2);

        Assert.Equal("ghs_tenant_a", a.Value);
        Assert.Equal("ghs_tenant_b", b.Value);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Invalidate_ForcesAReExchange()
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(HttpStatusCode.Created, TokenJson("ghs_first", DateTimeOffset.UtcNow.AddHours(1)))
            .Enqueue(HttpStatusCode.Created, TokenJson("ghs_second", DateTimeOffset.UtcNow.AddHours(1)));

        var (service, _, _) = NewService(handler);

        await service.GetInstallationTokenAsync(42);
        service.Invalidate(42);
        var refreshed = await service.GetInstallationTokenAsync(42);

        Assert.Equal("ghs_second", refreshed.Value);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task FailedExchange_ReturnsAResultFailure_NotAnException()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(HttpStatusCode.Unauthorized);

        var (service, _, _) = NewService(handler);

        var result = await service.GetInstallationTokenAsync(42);

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.TokenExchangeFailed", result.Errors[0].Code);
    }
}
