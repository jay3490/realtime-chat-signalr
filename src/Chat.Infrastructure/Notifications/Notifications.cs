using System.Net.Http.Json;
using Chat.Infrastructure.Presence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Chat.Infrastructure.Notifications;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>
    /// Where mention notifications are POSTed as JSON. When empty they are only logged,
    /// which is enough to see the service working without an endpoint to call.
    /// </summary>
    public string WebhookUrl { get; set; } = string.Empty;

    /// <summary>Characters of the message included in a notification.</summary>
    public int ExcerptLength { get; set; } = 140;

    /// <summary>How long a sent notification is remembered, so a replayed event does not send it again.</summary>
    public int DeduplicationHours { get; set; } = 24;

    /// <summary>
    /// Run the notifier inside the API. Only meaningful without Kafka, when the API is a single
    /// process; with Kafka the Chat.Notifications service does this.
    /// </summary>
    public bool RunInProcess { get; set; }

    public TimeSpan DeduplicationWindow => TimeSpan.FromHours(DeduplicationHours);
}

/// <summary>Someone was mentioned while they had no open connection.</summary>
public sealed record MentionNotification(
    string UserName,
    Guid RoomId,
    long MessageId,
    string Sender,
    string Excerpt,
    DateTimeOffset SentAt);

/// <summary>
/// Delivers a notification outside the app. Implementations exist for a webhook and for the
/// log; an e-mail or push provider would be another implementation of this interface.
/// </summary>
public interface INotificationSender
{
    Task SendAsync(MentionNotification notification, CancellationToken cancellationToken);
}

/// <summary>Posts each notification as JSON. A non-success response is an error, so the event is retried.</summary>
public sealed class WebhookNotificationSender(HttpClient http, IOptions<NotificationOptions> options) : INotificationSender
{
    public async Task SendAsync(MentionNotification notification, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(new Uri(options.Value.WebhookUrl), notification, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

public sealed partial class LoggingNotificationSender(ILogger<LoggingNotificationSender> logger) : INotificationSender
{
    public Task SendAsync(MentionNotification notification, CancellationToken cancellationToken)
    {
        LogNotification(logger, notification.UserName, notification.Sender, notification.RoomId, notification.MessageId);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Notify {UserName}: mentioned by {Sender} in room {RoomId} (message {MessageId})")]
    private static partial void LogNotification(ILogger logger, string userName, string sender, Guid roomId, long messageId);
}

/// <summary>
/// Remembers which notifications went out. The event log is read at least once, so the same
/// mention can arrive twice; claiming it first means it is sent once.
/// </summary>
public interface INotificationLedger
{
    /// <summary>True if <paramref name="key"/> was not claimed before; it is claimed now.</summary>
    Task<bool> TryClaimAsync(string key, CancellationToken cancellationToken);

    /// <summary>Gives a claim back after the notification could not be sent, so a retry sends it.</summary>
    Task ReleaseAsync(string key, CancellationToken cancellationToken);
}

/// <summary>Claims shared by every notifier replica, as Redis keys that expire on their own.</summary>
public sealed class RedisNotificationLedger(
    IConnectionMultiplexer redis,
    IOptions<RedisOptions> redisOptions,
    IOptions<NotificationOptions> options) : INotificationLedger
{
    public Task<bool> TryClaimAsync(string key, CancellationToken cancellationToken) =>
        redis.GetDatabase().StringSetAsync(Key(key), "1", options.Value.DeduplicationWindow, When.NotExists);

    public Task ReleaseAsync(string key, CancellationToken cancellationToken) =>
        redis.GetDatabase().KeyDeleteAsync(Key(key));

    private RedisKey Key(string key) => $"{redisOptions.Value.KeyPrefix}notified:{key}";
}

/// <summary>Claims held by this process only; correct when a single notifier runs.</summary>
public sealed class InMemoryNotificationLedger(IMemoryCache cache, IOptions<NotificationOptions> options) : INotificationLedger
{
    private readonly object _gate = new();

    public Task<bool> TryClaimAsync(string key, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (cache.TryGetValue(key, out _))
            {
                return Task.FromResult(false);
            }

            cache.Set(key, true, options.Value.DeduplicationWindow);
            return Task.FromResult(true);
        }
    }

    public Task ReleaseAsync(string key, CancellationToken cancellationToken)
    {
        cache.Remove(key);
        return Task.CompletedTask;
    }
}
