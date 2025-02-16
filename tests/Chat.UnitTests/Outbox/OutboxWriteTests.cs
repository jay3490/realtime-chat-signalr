using Chat.Application.Common;
using Chat.Application.Messages;
using Chat.Application.Rooms;
using Chat.Application.Users;
using Chat.Contracts;
using Chat.UnitTests.Support;
using Microsoft.EntityFrameworkCore;

namespace Chat.UnitTests.Outbox;

public sealed class OutboxWriteTests : IAsyncLifetime
{
    private SqliteChatDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _database = await SqliteChatDatabase.CreateAsync();
        await _database.RegisterUsersAsync("alice", "bob");
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Registering_users_writes_one_event_each()
    {
        var events = await OutboxEventsAsync();

        Assert.Equal(["alice", "bob"], events.OfType<UserRegistered>().Select(e => e.UserName));
    }

    [Fact]
    public async Task A_sent_message_is_stored_with_its_database_id_and_resolved_mentions()
    {
        var roomId = await CreateChannelAsync("team", "alice");
        await JoinAsync(roomId, "bob");

        MessageDto sent;
        await using (var db = _database.NewContext())
        {
            sent = await new SendMessageHandler(db, _database.Clock)
                .HandleAsync(new SendMessageCommand(roomId, "alice", "@bob @carol look"), default);
        }

        var message = Assert.Single((await OutboxEventsAsync()).OfType<MessageSent>());
        Assert.Equal((sent.Id, roomId, "alice", "@bob @carol look"), (message.MessageId, message.RoomId, message.Sender, message.Body));
        Assert.Equal(["bob"], message.Mentions);
    }

    [Fact]
    public async Task Rows_are_numbered_in_the_order_the_events_happened()
    {
        var roomId = await CreateChannelAsync("team", "alice");
        await JoinAsync(roomId, "bob");

        await using var db = _database.NewContext();
        var types = await db.OutboxMessages.OrderBy(m => m.Id).Select(m => m.EventType).ToListAsync();

        Assert.Equal(["UserRegistered", "UserRegistered", "RoomCreated", "MemberJoined"], types);
    }

    [Fact]
    public async Task A_rejected_change_leaves_no_event_behind()
    {
        await CreateChannelAsync("team", "alice");

        // Raced past the existence check: the unique index rejects the insert.
        await using (var db = _database.NewContext())
        {
            db.Rooms.Add(Chat.Domain.Rooms.Room.CreateChannel("team", "bob", _database.Clock.GetUtcNow()));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        Assert.Single((await OutboxEventsAsync()).OfType<RoomCreated>());
    }

    [Fact]
    public async Task Signing_in_again_does_not_register_the_user_twice()
    {
        await using (var db = _database.NewContext())
        {
            await new SignInHandler(db, _database.Clock).HandleAsync(new SignInCommand("alice"), default);
        }

        Assert.Equal(2, (await OutboxEventsAsync()).OfType<UserRegistered>().Count());
    }

    [Fact]
    public async Task Payloads_are_envelopes_keyed_by_stream()
    {
        var roomId = await CreateChannelAsync("team", "alice");

        await using var db = _database.NewContext();
        var row = await db.OutboxMessages.SingleAsync(m => m.EventType == "RoomCreated");
        var envelope = ChatEventSerializer.Deserialize(row.Payload);

        Assert.Equal((EventStreams.Rooms, roomId.ToString(), row.EventId), (row.Stream, row.StreamKey, envelope.EventId));
        Assert.Null(row.PublishedAt);
        await Assert.ThrowsAsync<ConflictException>(() => CreateChannelAsync("team", "bob"));
    }

    private async Task<List<IChatEvent>> OutboxEventsAsync()
    {
        await using var db = _database.NewContext();
        var payloads = await db.OutboxMessages.OrderBy(m => m.Id).Select(m => m.Payload).ToListAsync();
        return payloads.Select(p => ChatEventSerializer.Deserialize(p).Event).ToList();
    }

    private async Task<Guid> CreateChannelAsync(string name, string user)
    {
        await using var db = _database.NewContext();
        return await new CreateChannelHandler(db, _database.Clock).HandleAsync(new CreateChannelCommand(name, user), default);
    }

    private async Task JoinAsync(Guid roomId, string user)
    {
        await using var db = _database.NewContext();
        await new JoinChannelHandler(db, _database.Clock).HandleAsync(new JoinChannelCommand(roomId, user), default);
    }
}
