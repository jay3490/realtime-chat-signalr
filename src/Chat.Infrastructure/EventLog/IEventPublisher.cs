namespace Chat.Infrastructure.EventLog;

/// <summary>An event ready to append to the log: the serialized envelope plus routing.</summary>
public sealed record OutgoingEvent(Guid EventId, string EventType, string Stream, string Key, string Payload);

/// <param name="Acknowledged">
/// How many events at the start of the batch the log has durably accepted. Events after the
/// first failure are not counted even if they were accepted, so they are sent again and
/// per-key order is never broken; consumers drop the duplicates by event id.
/// </param>
public sealed record PublishResult(int Acknowledged, string? Error = null);

/// <summary>Appends events to the log in the order given.</summary>
public interface IEventPublisher
{
    Task<PublishResult> PublishAsync(IReadOnlyList<OutgoingEvent> batch, CancellationToken cancellationToken);
}
