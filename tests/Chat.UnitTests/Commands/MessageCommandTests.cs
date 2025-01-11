using Chat.Application.Common;
using Chat.Application.Messages;
using Chat.Application.Rooms;
using Chat.Domain.Common;
using Chat.Domain.Messages;
using Chat.Domain.Rooms;
using Chat.UnitTests.Support;
using Microsoft.EntityFrameworkCore;

namespace Chat.UnitTests.Commands;

public sealed class MessageCommandTests : IAsyncLifetime
{
    private SqliteChatDatabase _database = null!;
    private Guid _roomId;

    public async Task InitializeAsync()
    {
        _database = await SqliteChatDatabase.CreateAsync();
        await _database.RegisterUsersAsync("alice", "bob");

        await using var db = _database.NewContext();
        _roomId = await new CreateChannelHandler(db, _database.Clock)
            .HandleAsync(new CreateChannelCommand("team", "alice"), default);
        _database.Events.Clear();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Sending_stores_the_message_and_dispatches_after_the_id_is_assigned()
    {
        var sent = await SendAsync("alice", "  hello team  ");

        Assert.True(sent.Id > 0);
        Assert.Equal("hello team", sent.Body);
        Assert.Equal(_database.Clock.GetUtcNow(), sent.SentAt);

        var posted = Assert.IsType<MessagePosted>(Assert.Single(_database.Events.Dispatched));
        Assert.Equal(sent.Id, posted.Message.Id);
        Assert.Equal([sent.Id], _database.Events.MessageIdsAtDispatch);

        await using var db = _database.NewContext();
        var stored = await db.Messages.SingleAsync();
        Assert.Equal(("alice", "hello team"), (stored.Sender, stored.Body));
    }

    [Fact]
    public async Task Message_ids_increase_in_send_order()
    {
        var first = await SendAsync("alice", "one");
        var second = await SendAsync("alice", "two");

        Assert.True(second.Id > first.Id);
    }

    [Fact]
    public async Task Non_members_cannot_send()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync("bob", "let me in"));
        Assert.Empty(_database.Events.Dispatched);
    }

    [Fact]
    public async Task Sending_to_an_unknown_room_is_not_found()
    {
        await using var db = _database.NewContext();
        var handler = new SendMessageHandler(db, _database.Clock);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new SendMessageCommand(Guid.NewGuid(), "alice", "hi"), default));
    }

    [Fact]
    public async Task Blank_messages_are_rejected()
    {
        await Assert.ThrowsAsync<DomainRuleException>(() => SendAsync("alice", "   "));
    }

    [Fact]
    public async Task Mark_read_advances_the_marker_and_raises_a_receipt()
    {
        var message = await SendAsync("alice", "hello");
        _database.Clock.Advance(TimeSpan.FromSeconds(5));
        _database.Events.Clear();

        var advanced = await MarkReadAsync("alice", message.Id);

        Assert.True(advanced);
        var receipt = Assert.IsType<MessagesRead>(Assert.Single(_database.Events.Dispatched));
        Assert.Equal(("alice", message.Id), (receipt.UserName, receipt.LastReadMessageId));

        await using var db = _database.NewContext();
        var member = await db.Set<RoomMember>().SingleAsync(m => m.UserName == "alice");
        Assert.Equal(message.Id, member.LastReadMessageId);
        Assert.Equal(_database.Clock.GetUtcNow(), member.LastReadAt);
    }

    [Fact]
    public async Task Mark_read_with_an_older_message_is_ignored()
    {
        var older = await SendAsync("alice", "one");
        var newer = await SendAsync("alice", "two");
        await MarkReadAsync("alice", newer.Id);
        _database.Events.Clear();

        var advanced = await MarkReadAsync("alice", older.Id);

        Assert.False(advanced);
        Assert.Empty(_database.Events.Dispatched);
    }

    [Fact]
    public async Task Mark_read_rejects_a_message_from_another_room()
    {
        await using (var db = _database.NewContext())
        {
            var otherRoom = await new CreateChannelHandler(db, _database.Clock)
                .HandleAsync(new CreateChannelCommand("elsewhere", "alice"), default);
            var foreign = await new SendMessageHandler(db, _database.Clock)
                .HandleAsync(new SendMessageCommand(otherRoom, "alice", "hi"), default);

            await Assert.ThrowsAsync<NotFoundException>(() => MarkReadAsync("alice", foreign.Id));
        }
    }

    [Fact]
    public async Task Non_members_cannot_mark_read()
    {
        var message = await SendAsync("alice", "hello");

        await Assert.ThrowsAsync<ForbiddenException>(() => MarkReadAsync("bob", message.Id));
    }

    private async Task<MessageDto> SendAsync(string sender, string body)
    {
        await using var db = _database.NewContext();
        return await new SendMessageHandler(db, _database.Clock)
            .HandleAsync(new SendMessageCommand(_roomId, sender, body), default);
    }

    private async Task<bool> MarkReadAsync(string user, long messageId)
    {
        await using var db = _database.NewContext();
        return await new MarkReadHandler(db, _database.Clock)
            .HandleAsync(new MarkReadCommand(_roomId, user, messageId), default);
    }
}
