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

/// <summary>
/// Reads the <see cref="InMemoryEventLog"/>. Every stream has a single partition, 0, and
/// progress is held by the consumer itself.
/// </summary>
public sealed class InMemoryEventConsumer(InMemoryEventLog log, ConsumerSettings settings) : IEventLogConsumer
{
    private readonly Dictionary<string, long> _positions = new(StringComparer.Ordinal);

    public void Subscribe(IReadOnlyCollection<string> streams)
    {
        ArgumentNullException.ThrowIfNull(streams);
        foreach (var stream in streams)
        {
            // Without a resume callback a new consumer starts at the end, as a new Kafka group does.
            _positions[stream] = settings.ResumeAt is null
                ? log.Length(stream)
                : settings.ResumeAt(stream, 0) ?? 0;
        }
    }

    public ConsumedEvent? Consume(TimeSpan timeout)
    {
        var next = log.Next(_positions, timeout);
        if (next is null)
        {
            return null;
        }

        _positions[next.Stream] = next.Offset + 1;
        return ConsumedEvent.Parse(next.Stream, 0, next.Offset, next.Payload);
    }

    public void Commit(ConsumedEvent consumed)
    {
    }

    public void Close()
    {
    }

    public void Dispose()
    {
    }
}

public sealed class InMemoryEventConsumerFactory(InMemoryEventLog log) : IEventLogConsumerFactory
{
    public IEventLogConsumer Create(ConsumerSettings settings) => new InMemoryEventConsumer(log, settings);
}
