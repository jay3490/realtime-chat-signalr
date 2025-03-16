using System.Diagnostics.Metrics;
using Chat.Infrastructure.Observability;
using Chat.Infrastructure.ReadModel;
using Chat.UnitTests.Support;

namespace Chat.UnitTests.Observability;

public sealed class ChatMetricsTests : IDisposable
{
    private readonly ChatMetrics _metrics = TestMetrics.Create();
    private readonly MeterListener _listener = new();
    private readonly List<(string Name, double Value, Dictionary<string, object?> Tags)> _measurements = [];

    public ChatMetricsTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ChatMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.Start();
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void Counters_record_messages_rate_limits_and_notifications()
    {
        _metrics.MessageSent();
        _metrics.MessageSent();
        _metrics.RateLimited("SendMessage");
        _metrics.NotificationsSent(0);
        _metrics.NotificationsSent(3);

        Assert.Equal(2, Sum("chat.messages.sent"));
        Assert.Equal("SendMessage", Assert.Single(_measurements, m => m.Name == "chat.hub.rate_limited").Tags["method"]);
        Assert.Equal(3, Sum("chat.notifications.sent"));
    }

    [Fact]
    public void Outbox_rounds_count_published_events_and_failed_rounds()
    {
        _metrics.OutboxPublished(5, failed: false);
        _metrics.OutboxPublished(2, failed: true);

        Assert.Equal(7, Sum("chat.outbox.published"));
        Assert.Equal(1, Sum("chat.outbox.failures"));
    }

    [Fact]
    public void Projection_lag_and_connections_are_observed_on_collection()
    {
        var open = 4;
        _metrics.ObserveActiveConnections(() => open);
        _metrics.Projected(ProjectionOutcome.Applied, "MessageSent", TimeSpan.FromMilliseconds(250));

        _listener.RecordObservableInstruments();

        Assert.Equal(0.25, Last("chat.projection.lag"), precision: 3);
        Assert.Equal(4, Last("chat.connections.active"));
        var projected = Assert.Single(_measurements, m => m.Name == "chat.projection.events");
        Assert.Equal(("Applied", "MessageSent"), (projected.Tags["outcome"], projected.Tags["event_type"]));
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, object?>();
        foreach (var tag in tags)
        {
            copy[tag.Key] = tag.Value;
        }

        _measurements.Add((instrument.Name, value, copy));
    }

    private double Sum(string name) => _measurements.Where(m => m.Name == name).Sum(m => m.Value);

    private double Last(string name) => _measurements.Last(m => m.Name == name).Value;
}
