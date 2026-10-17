using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace TaskSphere.RateLimiting;

/// <summary>
/// One key for JWT users and agents alike: both put their user id in NameIdentifier, so an agent
/// has its own budget, separate from its owner's.
/// </summary>
public static class RateLimitKeys
{
    public static string For(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true
        && context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } userId
            ? $"user:{userId}"
            : Ip(context);

    public static string Ip(HttpContext context) =>
        $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}
