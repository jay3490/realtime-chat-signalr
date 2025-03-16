using System.Diagnostics.Metrics;
using Chat.Infrastructure.ReadModel;

namespace Chat.Infrastructure.Observability;

/// <summary>
/// The application's own instruments, on the <c>Chat</c> meter. Exported to Prometheus with
/// dots turned into underscores, e.g. <c>chat_messages_sent_total</c>.
/// </summary>
public sealed class ChatMetrics
{
    public const string MeterName = "Chat";

    private readonly Meter _meter;
    private readonly Counter<long> _messagesSent;
    private readonly Counter<long> _rateLimited;
    private readonly Counter<long> _outboxPublished;
    private readonly Counter<long> _outboxFailures;
    private readonly Counter<long> _projected;
    private readonly Counter<long> _notificationsSent;
    private double _projectionLagSeconds;

    public ChatMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _meter = meterFactory.Create(MeterName);

        _messagesSent = _meter.CreateCounter<long>(
            "chat.messages.sent", "{message}", "Messages committed by this instance.");
        _rateLimited = _meter.CreateCounter<long>(
            "chat.hub.rate_limited", "{invocation}", "Hub invocations refused by the per-connection rate limit.");
        _outboxPublished = _meter.CreateCounter<long>(
            "chat.outbox.published", "{event}", "Outbox rows acknowledged by the event log.");
        _outboxFailures = _meter.CreateCounter<long>(
            "chat.outbox.failures", "{batch}", "Outbox rounds that left rows unpublished.");
        _projected = _meter.CreateCounter<long>(
            "chat.projection.events", "{event}", "Events handled by the read model projection, by outcome.");
        _notificationsSent = _meter.CreateCounter<long>(
            "chat.notifications.sent", "{notification}", "Mention notifications sent to offline users.");
        _meter.CreateObservableGauge(
            "chat.projection.lag",
            () => Volatile.Read(ref _projectionLagSeconds),
            "s",
            "Time between the most recently projected event happening and it being applied to the read model.");
    }

    /// <summary>Reports the connections open on this instance; called once by whoever tracks them.</summary>
    public void ObserveActiveConnections(Func<int> count) =>
        _meter.CreateObservableGauge(
            "chat.connections.active", count, "{connection}", "SignalR connections open on this instance.");

    public void MessageSent() => _messagesSent.Add(1);

    public void RateLimited(string method) => _rateLimited.Add(1, new KeyValuePair<string, object?>("method", method));

    public void OutboxPublished(int count, bool failed)
    {
        if (count > 0)
        {
            _outboxPublished.Add(count);
        }

        if (failed)
        {
            _outboxFailures.Add(1);
        }
    }

    public void Projected(ProjectionOutcome outcome, string? eventType, TimeSpan lag)
    {
        _projected.Add(
            1,
            new KeyValuePair<string, object?>("outcome", outcome.ToString()),
            new KeyValuePair<string, object?>("event_type", eventType ?? "unknown"));
        Volatile.Write(ref _projectionLagSeconds, Math.Max(0, lag.TotalSeconds));
    }

    public void NotificationsSent(int count)
    {
        if (count > 0)
        {
            _notificationsSent.Add(count);
        }
    }
}
