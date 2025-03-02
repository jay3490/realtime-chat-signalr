using Chat.Application.Common;
using Chat.Application.Messages;
using Chat.Application.Rooms;
using Chat.Contracts;
using Chat.Domain.Messages;
using Chat.UnitTests.Support;
using Microsoft.EntityFrameworkCore;

namespace Chat.UnitTests.Commands;

public sealed class DeliveryCommandTests : IAsyncLifetime
{
    private SqliteChatDatabase _database = null!;
    private Guid _roomId;
    private long _first;
    private long _second;

    public async Task InitializeAsync()
    {
        _database = await SqliteChatDatabase.CreateAsync();
        await _database.RegisterUsersAsync("alice", "bob", "carol");

        await using var db = _database.NewContext();
        _roomId = await new OpenDirectConversationHandler(db, _database.Clock)
            .HandleAsync(new OpenDirectConversationCommand("alice", "bob"), default);
        var send = new SendMessageHandler(db, _database.Clock);
        _first = (await send.HandleAsync(new SendMessageCommand(_roomId, "alice", "one"), default)).Id;
        _second = (await send.HandleAsync(new SendMessageCommand(_roomId, "alice", "two"), default)).Id;
        _database.Events.Clear();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Each_device_has_its_own_marker_that_only_moves_forward()
    {
        Assert.True(await DeliverAsync("bob", "phone", _second));
        Assert.False(await DeliverAsync("bob", "phone", _first));
        Assert.True(await DeliverAsync("bob", "laptop", _first));

        await using var db = _database.NewContext();
        var markers = await db.DeliveryMarkers.OrderBy(m => m.DeviceId).ToListAsync();
        Assert.Equal([("laptop", _first), ("phone", _second)], markers.Select(m => (m.DeviceId, m.LastDeliveredMessageId)));
        Assert.Equal(2, _database.Events.Dispatched.OfType<MessagesDelivered>().Count());
    }

    [Fact]
    public async Task Deliveries_are_published_per_device()
    {
        await DeliverAsync("bob", "phone", _second);

        await using var db = _database.NewContext();
        var row = await db.OutboxMessages.SingleAsync(m => m.EventType == nameof(DeliveryRecorded));
        var delivered = (DeliveryRecorded)ChatEventSerializer.Deserialize(row.Payload).Event;

        Assert.Equal(("bob", "phone", _second), (delivered.UserName, delivered.DeviceId, delivered.LastDeliveredMessageId));
    }

    [Fact]
    public async Task Only_members_can_report_deliveries()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => DeliverAsync("carol", "tablet", _first));
    }

    [Fact]
    public async Task The_message_must_be_in_the_room()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => DeliverAsync("bob", "phone", _second + 100));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("a/b")]
    public async Task Malformed_device_ids_are_rejected(string deviceId)
    {
        await Assert.ThrowsAsync<RequestValidationException>(() => DeliverAsync("bob", deviceId, _first));
    }

    private async Task<bool> DeliverAsync(string user, string deviceId, long messageId)
    {
        await using var db = _database.NewContext();
        return await new RecordDeliveryHandler(db, _database.Clock)
            .HandleAsync(new RecordDeliveryCommand(_roomId, user, deviceId, messageId), default);
    }
}
