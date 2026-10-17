using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TaskSphere.RateLimiting;

namespace TaskSphere.Tests.Domain;

public class RateLimitKeysTests
{
    private static DefaultHttpContext Context(ClaimsPrincipal? user = null, string? ip = "10.0.0.7")
    {
        var context = new DefaultHttpContext();
        if (user is not null) context.User = user;
        context.Connection.RemoteIpAddress = ip is null ? null : IPAddress.Parse(ip);
        return context;
    }

    private static ClaimsPrincipal SignedIn(string userId, string scheme) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], scheme));

    [Fact]
    public void A_jwt_user_is_keyed_by_user_id()
    {
        Assert.Equal("user:human-1", RateLimitKeys.For(Context(SignedIn("human-1", "Bearer"))));
    }

    [Fact]
    public void An_agent_is_keyed_by_its_own_user_id_not_its_owner()
    {
        // AgentTokenAuthenticationHandler puts the agent's user id in NameIdentifier.
        Assert.Equal("user:agent-9", RateLimitKeys.For(Context(SignedIn("agent-9", "AgentToken"))));
    }

    [Fact]
    public void An_anonymous_caller_is_keyed_by_ip()
    {
        Assert.Equal("ip:10.0.0.7", RateLimitKeys.For(Context()));
    }

    [Fact]
    public void An_identity_that_is_not_authenticated_is_keyed_by_ip_even_with_a_name_claim()
    {
        // No authentication type → IsAuthenticated is false, which is what a failed token leaves.
        var unauthenticated = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "x")]));

        Assert.Equal("ip:10.0.0.7", RateLimitKeys.For(Context(unauthenticated)));
    }

    [Fact]
    public void A_missing_remote_address_shares_one_unknown_bucket()
    {
        Assert.Equal("ip:unknown", RateLimitKeys.For(Context(ip: null)));
    }

    [Fact]
    public void Ip_ignores_a_signed_in_user()
    {
        Assert.Equal("ip:10.0.0.7", RateLimitKeys.Ip(Context(SignedIn("human-1", "Bearer"))));
    }

    [Fact]
    public void Options_default_to_the_spec_values()
    {
        var o = new RateLimitingOptions();

        Assert.Equal(
            (10, 10, 20, 300, 60, 30),
            (o.LoginPerMinute, o.AccountPer15Minutes, o.InvitesPerHour, o.RequestsPerMinute, o.WritesPerMinute, o.ChatMessagesPerMinute));
    }
}
