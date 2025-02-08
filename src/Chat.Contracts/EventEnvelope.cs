using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chat.Contracts;

/// <summary>
/// An event together with the metadata every consumer needs: a unique id for deduplication,
/// the type name used to deserialize it, and where it belongs in the log.
/// </summary>
public sealed record EventEnvelope(Guid EventId, string EventType, string StreamKey, DateTimeOffset OccurredAt, IChatEvent Event)
{
    public string Stream => Event.Stream;

    public static EventEnvelope Wrap(IChatEvent chatEvent, Guid eventId)
    {
        ArgumentNullException.ThrowIfNull(chatEvent);
        return new EventEnvelope(eventId, ChatEventSerializer.TypeNameOf(chatEvent), chatEvent.StreamKey, chatEvent.OccurredAt, chatEvent);
    }
}

/// <summary>Raised when a payload names an event type this build does not know.</summary>
public sealed class UnknownEventTypeException(string eventType)
    : Exception($"Unknown event type '{eventType}'.")
{
    public string EventType { get; } = eventType;
}

/// <summary>
/// Reads and writes envelopes as JSON: <c>{ eventId, eventType, streamKey, occurredAt, data }</c>.
/// The type name is the record's simple name, so renaming an event record is a breaking change.
/// </summary>
public static class ChatEventSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, Type> TypesByName = new[]
    {
        typeof(RoomCreated),
        typeof(MemberJoined),
        typeof(MessageSent),
        typeof(MessageEdited),
        typeof(MessageDeleted),
        typeof(ReadReceiptRecorded),
        typeof(DeliveryRecorded),
        typeof(UserRegistered),
    }.ToDictionary(t => t.Name, StringComparer.Ordinal);

    public static IReadOnlyCollection<string> KnownTypes => TypesByName.Keys;

    public static string TypeNameOf(IChatEvent chatEvent)
    {
        ArgumentNullException.ThrowIfNull(chatEvent);
        var name = chatEvent.GetType().Name;
        return TypesByName.ContainsKey(name) ? name : throw new UnknownEventTypeException(name);
    }

    public static string Serialize(EventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var data = JsonSerializer.SerializeToElement(envelope.Event, envelope.Event.GetType(), Options);
        var wire = new WireEnvelope(envelope.EventId, envelope.EventType, envelope.StreamKey, envelope.OccurredAt, data);
        return JsonSerializer.Serialize(wire, Options);
    }

    /// <exception cref="UnknownEventTypeException">The payload names a type this build does not know.</exception>
    /// <exception cref="JsonException">The payload is not a valid envelope.</exception>
    public static EventEnvelope Deserialize(string json)
    {
        var wire = JsonSerializer.Deserialize<WireEnvelope>(json, Options)
            ?? throw new JsonException("The payload is not an event envelope.");
        if (wire.EventType is null || wire.StreamKey is null)
        {
            throw new JsonException("The envelope is missing its type or stream key.");
        }

        if (!TypesByName.TryGetValue(wire.EventType, out var type))
        {
            throw new UnknownEventTypeException(wire.EventType);
        }

        var chatEvent = (IChatEvent?)wire.Data.Deserialize(type, Options)
            ?? throw new JsonException($"The data of a {wire.EventType} event is empty.");
        return new EventEnvelope(wire.EventId, wire.EventType, wire.StreamKey, wire.OccurredAt, chatEvent);
    }

    private sealed record WireEnvelope(
        Guid EventId,
        string? EventType,
        string? StreamKey,
        DateTimeOffset OccurredAt,
        [property: JsonRequired] JsonElement Data);
}
