using Chat.Application.Common;
using Chat.Application.Messages;
using Chat.Application.Rooms;
using Chat.Contracts;
using Chat.UnitTests.Support;
using Microsoft.EntityFrameworkCore;
using DomainMessages = Chat.Domain.Messages;

namespace Chat.UnitTests.Commands;

public sealed class MessageChangeCommandTests : IAsyncLifetime
{
    private SqliteChatDatabase _database = null!;
    private Guid _roomId;
    private long _messageId;

    public async Task InitializeAsync()
    {
        _database = await SqliteChatDatabase.CreateAsync();
        await _database.RegisterUsersAsync("alice", "bob", "carol");

        await using var db = _database.NewContext();
        _roomId = await new CreateChannelHandler(db, _database.Clock)
            .HandleAsync(new CreateChannelCommand("team", "alice"), default);
        await new JoinChannelHandler(db, _database.Clock).HandleAsync(new JoinChannelCommand(_roomId, "bob"), default);
        _messageId = (await new SendMessageHandler(db, _database.Clock)
            .HandleAsync(new SendMessageCommand(_roomId, "alice", "helo"), default)).Id;
        _database.Events.Clear();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task The_author_can_edit_and_the_change_is_stored_and_published()
    {
        _database.Clock.Advance(TimeSpan.FromMinutes(1));

        var edited = await EditAsync("alice", _messageId, "hello");

        Assert.Equal(("hello", 2, false), (edited.Body, edited.Revision, edited.IsDeleted));
        Assert.Equal(_database.Clock.GetUtcNow(), edited.EditedAt);
        Assert.IsType<DomainMessages.MessageEdited>(Assert.Single(_database.Events.Dispatched));

        var published = Assert.Single(await OutboxAsync<MessageEdited>());
        Assert.Equal((_messageId, "hello", 2), (published.MessageId, published.Body, published.Revision));
    }

    [Fact]
    public async Task An_unchanged_edit_writes_nothing()
    {
        var result = await EditAsync("alice", _messageId, "helo");

        Assert.Equal(1, result.Revision);
        Assert.Empty(_database.Events.Dispatched);
        Assert.Empty(await OutboxAsync<MessageEdited>());
    }

    [Fact]
    public async Task Another_member_cannot_edit_or_delete_your_message()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => EditAsync("bob", _messageId, "hijacked"));
        await Assert.ThrowsAsync<ForbiddenException>(() => DeleteAsync("bob", _messageId));
    }

    [Fact]
    public async Task Non_members_are_forbidden_before_the_message_is_looked_up()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => EditAsync("carol", 999_999, "probe"));
    }

    [Fact]
    public async Task Unknown_rooms_and_messages_are_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => EditAsync("alice", 999_999, "x"));

        await using var db = _database.NewContext();
        await Assert.ThrowsAsync<NotFoundException>(() => new MessageChangeHandlers(db, _database.Clock)
            .HandleAsync(new DeleteMessageCommand(Guid.NewGuid(), _messageId, "alice"), default));
    }

    [Fact]
    public async Task Deleting_clears_the_text_once_and_publishes_one_event()
    {
        Assert.True(await DeleteAsync("alice", _messageId));
        Assert.False(await DeleteAsync("alice", _messageId));

        await using var db = _database.NewContext();
        var stored = await db.Messages.SingleAsync(m => m.Id == _messageId);
        Assert.Equal((string.Empty, true), (stored.Body, stored.IsDeleted));
        Assert.Equal(_messageId, Assert.Single(await OutboxAsync<MessageDeleted>()).MessageId);
    }

    [Fact]
    public async Task Edits_after_the_window_are_rejected()
    {
        _database.Clock.Advance(DomainMessages.Message.EditWindow + TimeSpan.FromMinutes(1));

        await Assert.ThrowsAsync<Chat.Domain.Common.DomainRuleException>(() => EditAsync("alice", _messageId, "late"));
    }

    private async Task<MessageDto> EditAsync(string user, long messageId, string body)
    {
        await using var db = _database.NewContext();
        return await new MessageChangeHandlers(db, _database.Clock)
            .HandleAsync(new EditMessageCommand(_roomId, messageId, user, body), default);
    }

    private async Task<bool> DeleteAsync(string user, long messageId)
    {
        await using var db = _database.NewContext();
        return await new MessageChangeHandlers(db, _database.Clock)
            .HandleAsync(new DeleteMessageCommand(_roomId, messageId, user), default);
    }

    private async Task<List<T>> OutboxAsync<T>()
        where T : IChatEvent
    {
        await using var db = _database.NewContext();
        var payloads = await db.OutboxMessages.Where(m => m.EventType == typeof(T).Name).Select(m => m.Payload).ToListAsync();
        return payloads.Select(p => (T)ChatEventSerializer.Deserialize(p).Event).ToList();
    }
}
