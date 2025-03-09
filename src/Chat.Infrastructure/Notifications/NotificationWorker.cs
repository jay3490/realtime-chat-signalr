using Chat.Contracts;
using Chat.Infrastructure.EventLog;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chat.Infrastructure.Notifications;

/// <summary>
/// Reads room events in its own consumer group and hands them to <see cref="MentionNotifier"/>.
/// Progress is the group's committed offset; a new group starts at the end of the log, so
/// deploying the service does not notify anyone about old mentions.
/// </summary>
public sealed partial class NotificationWorker(
    IEventLogConsumerFactory consumers,
    MentionNotifier notifier,
    TimeProvider clock,
    ILogger<NotificationWorker> logger) : BackgroundService
{
    public const string GroupId = "chat-notifications";

    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(1);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Factory.StartNew(() => RunAsync(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        using var consumer = consumers.Create(new ConsumerSettings(GroupId));
        consumer.Subscribe([EventStreams.Rooms]);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var consumed = consumer.Consume(PollTimeout);
                if (consumed is null)
                {
                    continue;
                }

                if (consumed.Envelope is { } envelope)
                {
                    await NotifyWithRetryAsync(envelope, stoppingToken);
                }

                consumer.Commit(consumed);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; an uncommitted event is read again on the next start.
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task NotifyWithRetryAsync(EventEnvelope envelope, CancellationToken stoppingToken)
    {
        var backoff = TimeSpan.FromSeconds(1);
        while (true)
        {
            try
            {
                await notifier.HandleAsync(envelope, stoppingToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogNotifyFailed(logger, ex, envelope.EventId, backoff.TotalSeconds);
                await Task.Delay(backoff, clock, stoppingToken);
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Notifying about event {EventId} failed; retrying in {DelaySeconds}s")]
    private static partial void LogNotifyFailed(ILogger logger, Exception exception, Guid eventId, double delaySeconds);
}

public static class NotificationSetup
{
    /// <summary>
    /// Registers the mention notifier and its worker. Presence must already be registered
    /// (<c>AddChatPresence</c>); with Redis the deduplication ledger is shared through it too.
    /// </summary>
    public static IServiceCollection AddMentionNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(NotificationOptions.SectionName);
        services.Configure<NotificationOptions>(section);
        var options = section.Get<NotificationOptions>() ?? new NotificationOptions();

        if (string.IsNullOrWhiteSpace(options.WebhookUrl))
        {
            services.AddSingleton<INotificationSender, LoggingNotificationSender>();
        }
        else
        {
            services.AddHttpClient<INotificationSender, WebhookNotificationSender>(http => http.Timeout = TimeSpan.FromSeconds(10));
        }

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Redis")))
        {
            services.AddMemoryCache();
            services.AddSingleton<INotificationLedger, InMemoryNotificationLedger>();
        }
        else
        {
            services.AddSingleton<INotificationLedger, RedisNotificationLedger>();
        }

        services.AddSingleton<MentionNotifier>();
        services.AddHostedService<NotificationWorker>();
        return services;
    }
}
