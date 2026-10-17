using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskSphere.Extensions;
using TaskSphere.RateLimiting;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// The production limiter (AddTaskSphereRateLimiting) and the production JWT setup, in the
/// production order — authentication, then the limiter, then authorization — in front of probe
/// endpoints that carry the same policies the real actions do. No database: the limiter needs none.
/// </summary>
internal sealed class RateLimitTestHost : IAsyncDisposable
{
    public WebApplication App { get; }
    public HttpClient Http { get; }

    private RateLimitTestHost(WebApplication app)
    {
        App = app;
        Http = app.GetTestClient();
    }

    public static async Task<RateLimitTestHost> StartAsync(Dictionary<string, string?> limits)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();

        var settings = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "test-key-that-is-long-enough-for-hmac-sha256-signing",
            ["Jwt:Issuer"] = "TaskSphereAPI",
            ["Jwt:Audience"] = "TaskSphereClient",
        };
        foreach (var (k, v) in limits) settings[$"{RateLimitingOptions.Section}:{k}"] = v;
        builder.Configuration.AddInMemoryCollection(settings);

        builder.Services.AddJwtAuthentication(builder.Configuration);
        builder.Services.AddAuthorization();
        builder.Services.AddTaskSphereRateLimiting(builder.Configuration);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();

        app.MapPost("/probe/login", () => "ok").RequireRateLimiting(RateLimitPolicies.Login);
        app.MapPost("/probe/register", () => "ok").RequireRateLimiting(RateLimitPolicies.Account);
        app.MapPost("/probe/forgot", () => "ok").RequireRateLimiting(RateLimitPolicies.Account);
        app.MapPost("/probe/invite", () => "ok").RequireAuthorization().RequireRateLimiting(RateLimitPolicies.Invite);
        app.MapGet("/probe/read", () => "ok");
        app.MapPost("/probe/write", () => "ok");
        app.MapPost("/hubs/chat/negotiate", () => "ok");

        await app.StartAsync();
        return new RateLimitTestHost(app);
    }

    public HttpRequestMessage Request(HttpMethod method, string path, string? bearer = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (bearer is not null) request.Headers.Authorization = new("Bearer", bearer);
        return request;
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
    }
}
