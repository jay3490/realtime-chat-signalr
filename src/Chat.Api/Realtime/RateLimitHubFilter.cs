using Chat.Application.Realtime;
using Chat.Infrastructure.Observability;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Chat.Api.Realtime;

/// <summary>
/// Limits how fast each connection can send, edit and delete messages. Every connection gets
/// a token bucket when it connects; an invocation that finds the bucket empty is refused with
/// a <see cref="HubException"/> and never reaches the hub. Typing notifications are throttled
/// separately and reads are not limited.
/// </summary>
internal sealed class RateLimitHubFilter(IOptions<RealtimeLimitsOptions> options, TimeProvider clock, ChatMetrics metrics) : IHubFilter
{
    private const string BucketItem = "chat.rateLimit";

    private static readonly HashSet<string> LimitedMethods = new(StringComparer.Ordinal)
    {
        nameof(ChatHub.SendMessage),
        nameof(ChatHub.EditMessage),
        nameof(ChatHub.DeleteMessage),
    };

    public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        var limits = options.Value;
        context.Context.Items[BucketItem] = new TokenBucket(clock, limits.MessagesPerSecond, limits.MessageBurst);
        return next(context);
    }

    public ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (LimitedMethods.Contains(invocationContext.HubMethodName)
            && invocationContext.Context.Items.TryGetValue(BucketItem, out var item)
            && item is TokenBucket bucket
            && !bucket.TryTake())
        {
            metrics.RateLimited(invocationContext.HubMethodName);
            throw new HubException("You are sending messages too quickly. Wait a moment and try again.");
        }

        return next(invocationContext);
    }
}
