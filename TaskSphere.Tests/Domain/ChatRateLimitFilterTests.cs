using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using TaskSphere.Hubs;
using TaskSphere.RateLimiting;

namespace TaskSphere.Tests.Domain;

public class ChatRateLimitFilterTests
{
    private sealed class FakeCaller(string userId) : HubCallerContext
    {
        public override string ConnectionId => $"conn-{userId}";
        public override string? UserIdentifier => userId;
        public override ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private sealed class ProbeHub : Hub;

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static HubInvocationContext Invocation(string userId, string method) =>
        new(new FakeCaller(userId), new EmptyServiceProvider(), new ProbeHub(),
            typeof(ChatHub).GetMethod(method)!, Array.Empty<object?>());

    private static readonly Func<HubInvocationContext, ValueTask<object?>> Next = _ => ValueTask.FromResult<object?>("sent");

    private static ChatRateLimitFilter Filter(int perMinute) =>
        new(new RateLimitingOptions { ChatMessagesPerMinute = perMinute });

    [Fact]
    public async Task SendMessage_past_the_limit_throws_a_hub_exception_and_is_not_sent()
    {
        var filter = Filter(2);
        await filter.InvokeMethodAsync(Invocation("u1", nameof(ChatHub.SendMessage)), Next);
        await filter.InvokeMethodAsync(Invocation("u1", nameof(ChatHub.SendMessage)), Next);

        var sent = false;
        var ex = await Assert.ThrowsAsync<HubException>(async () =>
            await filter.InvokeMethodAsync(Invocation("u1", nameof(ChatHub.SendMessage)), _ =>
            {
                sent = true;
                return ValueTask.FromResult<object?>(null);
            }));

        Assert.Equal("You are sending messages too fast. Wait a moment.", ex.Message);
        Assert.False(sent);
    }

    [Fact]
    public async Task Joining_and_leaving_are_never_limited()
    {
        var filter = Filter(1);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal("sent", await filter.InvokeMethodAsync(Invocation("u1", nameof(ChatHub.JoinProject)), Next));
            Assert.Equal("sent", await filter.InvokeMethodAsync(Invocation("u1", nameof(ChatHub.LeaveProject)), Next));
        }
    }

    [Fact]
    public async Task Two_users_have_separate_budgets()
    {
        var filter = Filter(1);
        await filter.InvokeMethodAsync(Invocation("u1", nameof(ChatHub.SendMessage)), Next);

        Assert.Equal("sent", await filter.InvokeMethodAsync(Invocation("u2", nameof(ChatHub.SendMessage)), Next));
    }
}
