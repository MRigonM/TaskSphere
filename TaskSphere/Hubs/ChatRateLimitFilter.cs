using System.Threading.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using TaskSphere.RateLimiting;

namespace TaskSphere.Hubs;

/// <summary>
/// HTTP rate limiting never sees a hub invocation, so chat messages are limited here. Registered
/// as a singleton: the counters must outlive one invocation.
/// </summary>
public sealed class ChatRateLimitFilter : IHubFilter
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public ChatRateLimitFilter(RateLimitingOptions limits)
    {
        _limiter = PartitionedRateLimiter.Create<string, string>(userId =>
            RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.ChatMessagesPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (invocationContext.HubMethodName != nameof(ChatHub.SendMessage))
            return await next(invocationContext);

        var key = invocationContext.Context.UserIdentifier ?? invocationContext.Context.ConnectionId;
        using var lease = _limiter.AttemptAcquire(key);

        if (!lease.IsAcquired)
            throw new HubException("You are sending messages too fast. Wait a moment.");

        return await next(invocationContext);
    }
}
