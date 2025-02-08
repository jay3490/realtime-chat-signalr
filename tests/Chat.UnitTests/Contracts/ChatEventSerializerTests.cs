using System.Text.Json;
using Chat.Contracts;

namespace Chat.UnitTests.Contracts;

public class ChatEventSerializerTests
{
    private static readonly DateTimeOffset At = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Room = new("0b9e8a3c-5f0e-4f55-9d0e-2b6f3f1a7c11");

    private static readonly IChatEvent[] Samples =
    [
        new RoomCreated(Room, "Direct", null, ["alice", "bob"], "alice", At),
        new MemberJoined(Room, "carol", At),
        new MessageSent(Room, 42, "alice", "hi @bob", ["bob"], At),
        new MessageEdited(Room, 42, "alice", "hi @bob!", 2, At),
        new MessageDeleted(Room, 42, "alice", At),
        new ReadReceiptRecorded(Room, "bob", 42, At),
        new DeliveryRecorded(Room, "bob", "phone-1", 42, At),
        new UserRegistered("carol", At),
    ];

    [Fact]
    public void Every_event_survives_a_round_trip()
    {
        foreach (var chatEvent in Samples)
        {
            var envelope = EventEnvelope.Wrap(chatEvent, Guid.NewGuid());

            var copy = ChatEventSerializer.Deserialize(ChatEventSerializer.Serialize(envelope));

            Assert.Equal((envelope.EventId, envelope.EventType, envelope.StreamKey, envelope.OccurredAt),
                (copy.EventId, copy.EventType, copy.StreamKey, copy.OccurredAt));
            Assert.Equal(JsonSerializer.Serialize<object>(chatEvent), JsonSerializer.Serialize<object>(copy.Event));
        }
    }

    [Fact]
    public void Every_known_type_has_a_sample()
    {
        Assert.Equal(
            ChatEventSerializer.KnownTypes.Order(StringComparer.Ordinal),
            Samples.Select(ChatEventSerializer.TypeNameOf).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Room_events_are_keyed_by_room_and_user_events_by_name()
    {
        var sent = EventEnvelope.Wrap(new MessageSent(Room, 1, "alice", "hi", [], At), Guid.NewGuid());
        var registered = EventEnvelope.Wrap(new UserRegistered("alice", At), Guid.NewGuid());

        Assert.Equal((EventStreams.Rooms, Room.ToString()), (sent.Stream, sent.StreamKey));
        Assert.Equal((EventStreams.Users, "alice"), (registered.Stream, registered.StreamKey));
    }

    [Fact]
    public void The_wire_format_names_the_type_and_nests_the_data()
    {
        var envelope = EventEnvelope.Wrap(new MemberJoined(Room, "carol", At), Guid.NewGuid());

        using var json = JsonDocument.Parse(ChatEventSerializer.Serialize(envelope));

        Assert.Equal("MemberJoined", json.RootElement.GetProperty("eventType").GetString());
        Assert.Equal("carol", json.RootElement.GetProperty("data").GetProperty("userName").GetString());
    }

    [Fact]
    public void Unknown_event_types_are_reported_by_name()
    {
        const string json = """{"eventId":"9a1f0c43-2a1b-4d0e-8f43-0c1b2a3d4e5f","eventType":"RoomArchived","streamKey":"x","occurredAt":"2026-03-01T09:00:00Z","data":{}}""";

        var error = Assert.Throws<UnknownEventTypeException>(() => ChatEventSerializer.Deserialize(json));

        Assert.Equal("RoomArchived", error.EventType);
    }

    [Fact]
    public void A_payload_without_data_is_rejected()
    {
        const string json = """{"eventId":"9a1f0c43-2a1b-4d0e-8f43-0c1b2a3d4e5f","eventType":"MemberJoined","streamKey":"x","occurredAt":"2026-03-01T09:00:00Z"}""";

        Assert.ThrowsAny<JsonException>(() => ChatEventSerializer.Deserialize(json));
    }
}
