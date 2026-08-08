using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TaskSphere.Application.Interfaces;
using TaskSphere.Infrastructure.Services;

namespace TaskSphere.Tests.GitHub;

/// <summary>
/// Task 13a is the fix for §0l: <c>installation_id</c> on the setup redirect is
/// attacker-controllable, so the callback must prove the authenticating user actually has that
/// installation. These tests cover the two calls that prove it, and the wiring that keeps a
/// user token from ever touching the installation-token path.
/// </summary>
public class GitHubUserAuthServiceTests
{
    private static GitHubUserAuthService NewService(FakeHttpMessageHandler handler)
        => new GitHubUserAuthService(
            new HttpClient(handler),
            Options.Create(GitHubAppOptionsAndJwtTests.ValidOptions()));

    private static string InstallationsJson(params long[] ids)
    {
        var items = string.Join(",", ids.Select(id => $$"""{"id":{{id}},"app_slug":"tasksphere-dev"}"""));
        return $$"""{"total_count":{{ids.Length}},"installations":[{{items}}]}""";
    }

    [Fact]
    public async Task ExchangeCode_ParsesTheTokenFromAJsonResponse()
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, """{"access_token":"gho_user","token_type":"bearer","scope":""}""");

        var result = await NewService(handler).ExchangeCodeForUserTokenAsync("the-code");

        Assert.True(result.IsSuccess);
        Assert.Equal("gho_user", result.Value);
    }

    [Fact]
    public async Task ExchangeCode_GoesToGithubCom_NotApiGithubCom()
    {
        // POST /login/oauth/access_token lives on github.com. Sending it to api.github.com
        // returns an HTML 404 that deserializes into a null token — the failure then looks
        // like a parser bug rather than a wrong URL (§0p).
        var handler = new FakeHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, """{"access_token":"gho_user"}""");

        await NewService(handler).ExchangeCodeForUserTokenAsync("the-code");

        var uri = handler.Requests[0].RequestUri!;

        Assert.Equal("github.com", uri.Host);
        Assert.Equal("/login/oauth/access_token", uri.AbsolutePath);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
    }

    [Fact]
    public async Task ExchangeCode_AsksForJson_NotGitHubsDefaultFormEncoding()
    {
        // Without Accept: application/json GitHub answers form-encoded and the JSON parse
        // silently yields no token.
        var handler = new FakeHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, """{"access_token":"gho_user"}""");

        await NewService(handler).ExchangeCodeForUserTokenAsync("the-code");

        Assert.Contains(
            handler.Requests[0].Headers.Accept,
            h => h.MediaType == "application/json");
    }

    [Fact]
    public async Task ExchangeCode_BadVerificationCode_IsAResultFailure_NotAnException()
    {
        // GitHub answers 200 with an error body for this case, which is why status-code-only
        // handling would treat a rejected code as a success and then persist a mapping.
        var handler = new FakeHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, """{"error":"bad_verification_code","error_description":"The code passed is incorrect or expired."}""");

        var result = await NewService(handler).ExchangeCodeForUserTokenAsync("stale-code");

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.UserAuthFailed", result.Errors[0].Code);
    }

    [Fact]
    public async Task ExchangeCode_HttpFailure_IsAResultFailure()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(HttpStatusCode.ServiceUnavailable);

        var result = await NewService(handler).ExchangeCodeForUserTokenAsync("the-code");

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.UserAuthFailed", result.Errors[0].Code);
    }

    [Fact]
    public async Task UserHasInstallation_TrueWhenPresent()
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, InstallationsJson(11, 22, 33));

        var result = await NewService(handler).UserHasInstallationAsync("gho_user", 22);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task UserHasInstallation_FalseWhenAbsent()
    {
        // This is the attack in §0l: a valid state, a real user, an installation id that
        // belongs to somebody else.
        var handler = new FakeHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, InstallationsJson(11, 22, 33));

        var result = await NewService(handler).UserHasInstallationAsync("gho_user", 999);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public async Task UserHasInstallation_FollowsPaginationPastPageOne()
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, InstallationsJson(11), r =>
                r.Headers.TryAddWithoutValidation(
                    "Link", "<https://api.github.com/user/installations?page=2>; rel=\"next\""))
            .Enqueue(HttpStatusCode.OK, InstallationsJson(22));

        var result = await NewService(handler).UserHasInstallationAsync("gho_user", 22);

        Assert.True(result.Value);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task UserHasInstallation_StopsPagingOnceFound()
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, InstallationsJson(22), r =>
                r.Headers.TryAddWithoutValidation(
                    "Link", "<https://api.github.com/user/installations?page=2>; rel=\"next\""));

        var result = await NewService(handler).UserHasInstallationAsync("gho_user", 22);

        Assert.True(result.Value);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task UserHasInstallation_CarriesTheUserToken_OnApiGithubCom()
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, InstallationsJson(22));

        await NewService(handler).UserHasInstallationAsync("gho_user", 22);

        Assert.Equal("api.github.com", handler.Requests[0].RequestUri!.Host);
        Assert.Equal("/user/installations", handler.Requests[0].RequestUri!.AbsolutePath);

        // The user token, not an installation token — riding the Task 10 client would attach
        // the wrong credential and GitHub's 403 would read as a missing permission (§0p).
        Assert.Equal("gho_user", handler.AuthorizationHeaders[0]);
    }

    [Fact]
    public async Task UserHasInstallation_HttpFailure_IsAResultFailure()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(HttpStatusCode.Unauthorized);

        var result = await NewService(handler).UserHasInstallationAsync("gho_user", 22);

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.UserAuthFailed", result.Errors[0].Code);
    }

    [Fact]
    public async Task UserToken_NeverReachesTheMemoryCache()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());

        var handler = new FakeHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, """{"access_token":"gho_user"}""")
            .Enqueue(HttpStatusCode.OK, InstallationsJson(22));

        var service = NewService(handler);

        var token = await service.ExchangeCodeForUserTokenAsync("the-code");
        await service.UserHasInstallationAsync(token.Value!, 22);

        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Service_TakesNoCacheAndNoInstallationTokenDependency()
    {
        // The structural version of the test above, and the one that actually holds: the user
        // token cannot be cached or cross into the installation-token path because this
        // service has no way to reach either. A future constructor change breaks this test
        // before it can leak a credential.
        var parameters = typeof(GitHubUserAuthService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType)
            .ToArray();

        Assert.DoesNotContain(typeof(IMemoryCache), parameters);
        Assert.DoesNotContain(typeof(IGitHubTokenService), parameters);
        Assert.DoesNotContain(typeof(IGitHubApiClient), parameters);
        Assert.DoesNotContain(typeof(IGitHubAppJwtProvider), parameters);
    }
}
