namespace Chat.Infrastructure.ReadModel;

/// <summary>
/// The last offset applied from one partition of a stream. Stored in the same transaction
/// as the rows the event changed, so after a restart the projection resumes exactly where
/// its committed state ends.
/// </summary>
public sealed class ProjectionCheckpoint
{
    public string Stream { get; set; } = string.Empty;

    public int Partition { get; set; }

    public long Offset { get; set; }
}

/// <summary>
/// Ids of applied events. The outbox relay is at-least-once, so the same event can appear
/// again at a later offset; this is what recognizes it.
/// </summary>
public sealed class ProcessedEvent
{
    public Guid EventId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; set; }
}
