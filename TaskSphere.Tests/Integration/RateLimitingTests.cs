using System.Net;
using System.Text.Json;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class RateLimitingTests
{
    private static readonly Guid Company = Guid.NewGuid();

    private static Task<RateLimitTestHost> Host(params (string Key, int Value)[] limits) =>
        RateLimitTestHost.StartAsync(limits.ToDictionary(l => l.Key, l => (string?)l.Value.ToString()));

    private static async Task<HttpStatusCode[]> Send(RateLimitTestHost host, int times, HttpMethod method, string path, string? bearer = null)
    {
        var codes = new HttpStatusCode[times];
        for (var i = 0; i < times; i++)
            codes[i] = (await host.Http.SendAsync(host.Request(method, path, bearer))).StatusCode;
        return codes;
    }

    [Fact]
    public async SystemTask.Task Login_past_its_limit_is_refused_with_429_retry_after_and_the_error_body()
    {
        await using var host = await Host(("LoginPerMinute", 2));
        await Send(host, 2, HttpMethod.Post, "/probe/login");

        var response = await host.Http.SendAsync(host.Request(HttpMethod.Post, "/probe/login"));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = Assert.Single(body.RootElement.EnumerateArray());
        Assert.Equal("RateLimit.Exceeded", error.GetProperty("code").GetString());
        Assert.Matches(@"^Too many requests\. Try again in \d+ seconds\.$", error.GetProperty("description").GetString());
    }

    [Fact]
    public async SystemTask.Task The_account_actions_share_one_bucket_per_ip()
    {
        await using var host = await Host(("AccountPer15Minutes", 2));

        var codes = new[]
        {
            (await host.Http.SendAsync(host.Request(HttpMethod.Post, "/probe/register"))).StatusCode,
            (await host.Http.SendAsync(host.Request(HttpMethod.Post, "/probe/forgot"))).StatusCode,
            (await host.Http.SendAsync(host.Request(HttpMethod.Post, "/probe/register"))).StatusCode,
        };

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], codes);
    }

    [Fact]
    public async SystemTask.Task Login_counts_by_ip_even_with_a_bearer_token()
    {
        // Two different signed-in users from one address share the login bucket.
        await using var host = await Host(("LoginPerMinute", 1));
        var a = McpTestHost.CreateJwt("user-a", Company);
        var b = McpTestHost.CreateJwt("user-b", Company);

        await Send(host, 1, HttpMethod.Post, "/probe/login", a);
        var second = await Send(host, 1, HttpMethod.Post, "/probe/login", b);

        Assert.Equal(HttpStatusCode.TooManyRequests, second[0]);
    }

    [Fact]
    public async SystemTask.Task Invite_counts_per_user()
    {
        await using var host = await Host(("InvitesPerHour", 1));
        var a = McpTestHost.CreateJwt("user-a", Company);
        var b = McpTestHost.CreateJwt("user-b", Company);

        var codes = new[]
        {
            (await Send(host, 1, HttpMethod.Post, "/probe/invite", a))[0],
            (await Send(host, 1, HttpMethod.Post, "/probe/invite", a))[0],
            (await Send(host, 1, HttpMethod.Post, "/probe/invite", b))[0],
        };

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.TooManyRequests, HttpStatusCode.OK], codes);
    }

    [Fact]
    public async SystemTask.Task Reads_do_not_spend_the_write_budget_and_writes_past_it_are_refused()
    {
        await using var host = await Host(("WritesPerMinute", 2));
        var token = McpTestHost.CreateJwt("user-a", Company);

        var reads = await Send(host, 5, HttpMethod.Get, "/probe/read", token);
        var writes = await Send(host, 3, HttpMethod.Post, "/probe/write", token);

        Assert.All(reads, c => Assert.Equal(HttpStatusCode.OK, c));
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], writes);
    }

    [Fact]
    public async SystemTask.Task All_requests_are_limited_per_user_and_users_do_not_share_a_bucket()
    {
        await using var host = await Host(("RequestsPerMinute", 2));
        var a = McpTestHost.CreateJwt("user-a", Company);
        var b = McpTestHost.CreateJwt("user-b", Company);

        var forA = await Send(host, 3, HttpMethod.Get, "/probe/read", a);
        var forB = await Send(host, 1, HttpMethod.Get, "/probe/read", b);

        Assert.Equal(HttpStatusCode.TooManyRequests, forA[2]);
        Assert.Equal(HttpStatusCode.OK, forB[0]);
    }

    [Fact]
    public async SystemTask.Task An_invalid_token_is_counted_by_ip_with_the_anonymous_traffic()
    {
        await using var host = await Host(("WritesPerMinute", 1));

        await Send(host, 1, HttpMethod.Post, "/probe/write");                         // anonymous, spends ip:unknown
        var invalid = await Send(host, 1, HttpMethod.Post, "/probe/write", "not-a-jwt");
        var valid = await Send(host, 1, HttpMethod.Post, "/probe/write", McpTestHost.CreateJwt("user-a", Company));

        Assert.Equal(HttpStatusCode.TooManyRequests, invalid[0]);
        Assert.Equal(HttpStatusCode.OK, valid[0]);
    }

    [Fact]
    public async SystemTask.Task The_chat_hub_is_exempt_from_the_global_limiter()
    {
        await using var host = await Host(("RequestsPerMinute", 1), ("WritesPerMinute", 1));

        var codes = await Send(host, 3, HttpMethod.Post, "/hubs/chat/negotiate");

        Assert.All(codes, c => Assert.Equal(HttpStatusCode.OK, c));
    }
}
