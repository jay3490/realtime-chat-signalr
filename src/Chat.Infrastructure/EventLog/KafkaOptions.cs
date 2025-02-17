using Chat.Contracts;

namespace Chat.Infrastructure.EventLog;

/// <summary>
/// Kafka settings. When <see cref="BootstrapServers"/> is empty the event log is kept in
/// process memory instead, which only works when one process both writes and consumes it
/// (local development and tests).
/// </summary>
public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = string.Empty;

    public string ClientId { get; set; } = "realtime-chat";

    public KafkaTopicOptions Topics { get; set; } = new();

    /// <summary>Partitions created for each topic. Events with the same key stay in one partition.</summary>
    public int Partitions { get; set; } = 6;

    public short ReplicationFactor { get; set; } = 1;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BootstrapServers);

    public string TopicFor(string stream) => stream switch
    {
        EventStreams.Rooms => Topics.Rooms,
        EventStreams.Users => Topics.Users,
        _ => throw new ArgumentOutOfRangeException(nameof(stream), stream, "Unknown event stream."),
    };

    public string StreamOf(string topic) =>
        string.Equals(topic, Topics.Rooms, StringComparison.Ordinal) ? EventStreams.Rooms
        : string.Equals(topic, Topics.Users, StringComparison.Ordinal) ? EventStreams.Users
        : throw new ArgumentOutOfRangeException(nameof(topic), topic, "Topic is not mapped to an event stream.");
}

public sealed class KafkaTopicOptions
{
    public string Rooms { get; set; } = "chat.room-events";

    public string Users { get; set; } = "chat.user-events";
}
