namespace Chat.Contracts;

/// <summary>
/// A fact recorded in the chat event log. Events are immutable once published, so a
/// change to one of these records must stay readable by consumers built against the old shape.
/// </summary>
public interface IChatEvent
{
    /// <summary>The stream the event belongs to; see <see cref="EventStreams"/>.</summary>
    string Stream { get; }

    /// <summary>
    /// Partition key within the stream. Events with the same key are delivered in the order
    /// they were written.
    /// </summary>
    string StreamKey { get; }

    DateTimeOffset OccurredAt { get; }
}

/// <summary>An event about one room, keyed by the room id so a room's history stays in order.</summary>
public interface IRoomEvent : IChatEvent
{
    Guid RoomId { get; }

    string IChatEvent.Stream => EventStreams.Rooms;

    string IChatEvent.StreamKey => RoomId.ToString("D");
}

/// <summary>An event about a user account, keyed by user name.</summary>
public interface IUserEvent : IChatEvent
{
    string UserName { get; }

    string IChatEvent.Stream => EventStreams.Users;

    string IChatEvent.StreamKey => UserName;
}

/// <summary>Logical stream names. Each one maps to a Kafka topic through configuration.</summary>
public static class EventStreams
{
    public const string Rooms = "rooms";

    public const string Users = "users";

    public static IReadOnlyList<string> All { get; } = [Rooms, Users];
}
