namespace Chat.Infrastructure.EventLog;

public sealed record LoggedEvent(string Stream, long Offset, string Key, string Payload);

/// <summary>
/// An append-only log per stream, held in memory. It stands in for Kafka when the API runs
/// as a single process: one partition per stream, nothing survives a restart.
/// </summary>
public sealed class InMemoryEventLog
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<LoggedEvent>> _streams = new(StringComparer.Ordinal);

    public long Append(string stream, string key, string payload)
    {
        lock (_gate)
        {
            if (!_streams.TryGetValue(stream, out var events))
            {
                events = [];
                _streams[stream] = events;
            }

            var logged = new LoggedEvent(stream, events.Count, key, payload);
            events.Add(logged);
            Monitor.PulseAll(_gate);
            return logged.Offset;
        }
    }

    public long Length(string stream)
    {
        lock (_gate)
        {
            return _streams.TryGetValue(stream, out var events) ? events.Count : 0;
        }
    }

    /// <summary>
    /// Returns the first event at or after the given positions, waiting up to
    /// <paramref name="timeout"/> for one to be appended; null if none arrives.
    /// </summary>
    public LoggedEvent? Next(IReadOnlyDictionary<string, long> positions, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(positions);
        var deadline = DateTime.UtcNow + timeout;
        lock (_gate)
        {
            while (true)
            {
                foreach (var (stream, position) in positions)
                {
                    if (_streams.TryGetValue(stream, out var events) && position < events.Count)
                    {
                        return events[(int)position];
                    }
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero || !Monitor.Wait(_gate, remaining))
                {
                    return null;
                }
            }
        }
    }
}

/// <summary>Publishes to the <see cref="InMemoryEventLog"/>; every append succeeds.</summary>
public sealed class InMemoryEventPublisher(InMemoryEventLog log) : IEventPublisher
{
    public Task<PublishResult> PublishAsync(IReadOnlyList<OutgoingEvent> batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        foreach (var item in batch)
        {
            log.Append(item.Stream, item.Key, item.Payload);
        }

        return Task.FromResult(new PublishResult(batch.Count));
    }
}
