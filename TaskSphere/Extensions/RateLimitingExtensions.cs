using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using TaskSphere.Hubs;
using TaskSphere.RateLimiting;

namespace TaskSphere.Extensions;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddTaskSphereRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var limits = configuration.GetSection(RateLimitingOptions.Section).Get<RateLimitingOptions>()
            ?? new RateLimitingOptions();

        services.AddSingleton(limits);
        services.AddSingleton<ChatRateLimitFilter>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;

            // Login and the account actions key by IP explicitly: a stray bearer header on an
            // anonymous endpoint must not move the count into a user bucket.
            options.AddPolicy(RateLimitPolicies.Login, context =>
                Fixed(RateLimitKeys.Ip(context), limits.LoginPerMinute, TimeSpan.FromMinutes(1)));
            options.AddPolicy(RateLimitPolicies.Account, context =>
                Fixed(RateLimitKeys.Ip(context), limits.AccountPer15Minutes, TimeSpan.FromMinutes(15)));
            options.AddPolicy(RateLimitPolicies.Invite, context =>
                Fixed(RateLimitKeys.For(context), limits.InvitesPerHour, TimeSpan.FromHours(1)));

            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    IsExempt(context)
                        ? RateLimitPartition.GetNoLimiter("exempt")
                        : Fixed(RateLimitKeys.For(context), limits.RequestsPerMinute, TimeSpan.FromMinutes(1))),
                // Every MCP tool call is a POST /mcp, so reads like list_tasks count here too.
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    IsExempt(context) || !IsWrite(context.Request.Method)
                        ? RateLimitPartition.GetNoLimiter("not-a-write")
                        : Fixed(RateLimitKeys.For(context), limits.WritesPerMinute, TimeSpan.FromMinutes(1))));
        });

        return services;
    }

    private static RateLimitPartition<string> Fixed(string key, int permits, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    // The chat connection's traffic is invisible to HTTP middleware anyway; ChatRateLimitFilter
    // limits the messages themselves.
    private static bool IsExempt(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/hubs/chat");

    private static bool IsWrite(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method)
        || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    /// <summary>The same [{code, description}] body every other error has, so the client reads it unchanged.</summary>
    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        var when = "in a moment";

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            when = $"in {seconds} seconds";
        }

        await response.WriteAsJsonAsync(
            new[] { new { code = "RateLimit.Exceeded", description = $"Too many requests. Try again {when}." } },
            cancellationToken);
    }
}
