using System.Text.Json;
using Chat.Contracts;

namespace Chat.Infrastructure.EventLog;

/// <summary>An event read from one partition of a stream; <see cref="Envelope"/> is null when it could not be read.</summary>
public sealed record ConsumedEvent(string Stream, int Partition, long Offset, EventEnvelope? Envelope, string? Error = null)
{
    /// <summary>Reads a payload; a malformed or unknown one becomes an event with an <see cref="Error"/>.</summary>
    public static ConsumedEvent Parse(string stream, int partition, long offset, string? payload)
    {
        try
        {
            return new ConsumedEvent(stream, partition, offset, ChatEventSerializer.Deserialize(payload ?? string.Empty));
        }
        catch (Exception ex) when (ex is JsonException or UnknownEventTypeException)
        {
            return new ConsumedEvent(stream, partition, offset, null, ex.Message);
        }
    }
}

/// <param name="GroupId">Consumers sharing a group id split the partitions between them.</param>
/// <param name="ResumeAt">
/// Where to start reading a partition when it is assigned: the consumer's own checkpoint, or
/// null for the first offset. When the callback itself is null the position stored in the
/// group is used, and a new group starts at the end of the log.
/// </param>
public sealed record ConsumerSettings(string GroupId, Func<string, int, long?>? ResumeAt = null);

/// <summary>
/// Reads the event log one event at a time. Consumption blocks, so callers run it on a
/// dedicated thread. <see cref="Commit"/> records progress in the log's own consumer group;
/// consumers that keep checkpoints elsewhere use it only so lag is visible to Kafka tools.
/// </summary>
public interface IEventLogConsumer : IDisposable
{
    void Subscribe(IReadOnlyCollection<string> streams);

    /// <summary>The next event, or null if none arrived within <paramref name="timeout"/>.</summary>
    ConsumedEvent? Consume(TimeSpan timeout);

    void Commit(ConsumedEvent consumed);

    /// <summary>Leaves the consumer group cleanly so its partitions are reassigned at once.</summary>
    void Close();
}

public interface IEventLogConsumerFactory
{
    IEventLogConsumer Create(ConsumerSettings settings);
}
