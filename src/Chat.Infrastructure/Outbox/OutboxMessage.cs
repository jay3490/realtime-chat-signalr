using Chat.Contracts;

namespace Chat.Infrastructure.Outbox;

/// <summary>
/// An event waiting to be published, written in the same transaction as the change that
/// raised it. The identity <see cref="Id"/> is the publish order.
/// </summary>
public sealed class OutboxMessage
{
    public const int MaxErrorLength = 1000;

    private OutboxMessage()
    {
    }

    public long Id { get; private set; }

    public Guid EventId { get; private set; }

    /// <summary>Logical stream (<see cref="EventStreams"/>); mapped to a topic when published.</summary>
    public string Stream { get; private set; } = string.Empty;

    public string StreamKey { get; private set; } = string.Empty;

    public string EventType { get; private set; } = string.Empty;

    /// <summary>The serialized <see cref="EventEnvelope"/>.</summary>
    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxMessage From(EventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return new OutboxMessage
        {
            EventId = envelope.EventId,
            Stream = envelope.Stream,
            StreamKey = envelope.StreamKey,
            EventType = envelope.EventType,
            Payload = ChatEventSerializer.Serialize(envelope),
            OccurredAt = envelope.OccurredAt,
        };
    }
}
