using Chat.Application.Common;
using Chat.Application.Messages;
using Chat.Application.Messages.Queries;
using Chat.Application.Rooms;
using Chat.Application.Rooms.Queries;
using Chat.Application.Users.Queries;
using Chat.Domain.Rooms;
using Chat.UnitTests.Support;

namespace Chat.UnitTests.Queries;

public sealed class QueryTests : IAsyncLifetime
{
    private SqliteChatDatabase _database = null!;
    private Guid _team;

    public async Task InitializeAsync()
    {
        _database = await SqliteChatDatabase.CreateAsync();
        await _database.RegisterUsersAsync("alice", "bob", "carol");

        await using var db = _database.NewContext();
        _team = await new CreateChannelHandler(db, _database.Clock).HandleAsync(new CreateChannelCommand("team", "alice"), default);
        await new JoinChannelHandler(db, _database.Clock).HandleAsync(new JoinChannelCommand(_team, "bob"), default);
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Unread_counts_exclude_own_messages_and_anything_already_read()
    {
        await SendAsync(_team, "alice", "one");
        var two = await SendAsync(_team, "alice", "two");
        await SendAsync(_team, "alice", "three");
        await SendAsync(_team, "bob", "bob's own message");
        await MarkReadAsync(_team, "bob", two.Id);

        var bobRooms = await new RoomSummaryHandlers(_database.NewReadContext())
            .HandleAsync(new GetMyRoomsQuery("bob"), default);
        var counts = await new GetUnreadCountsHandler(_database.NewReadContext())
            .HandleAsync(new GetUnreadCountsQuery("bob"), default);

        var team = Assert.Single(bobRooms);
        Assert.Equal(1, team.UnreadCount);
        Assert.Equal(two.Id, team.LastReadMessageId);
        Assert.Equal(new UnreadCountDto(_team, 1, two.Id), Assert.Single(counts));
    }

    [Fact]
    public async Task Room_list_names_direct_conversations_after_the_other_person_and_sorts_by_activity()
    {
        Guid direct;
        await using (var db = _database.NewContext())
        {
            direct = await new OpenDirectConversationHandler(db, _database.Clock)
                .HandleAsync(new OpenDirectConversationCommand("carol", "alice"), default);
        }

        await SendAsync(_team, "bob", "older");
        await SendAsync(direct, "carol", "newer");

        var rooms = await new RoomSummaryHandlers(_database.NewReadContext())
            .HandleAsync(new GetMyRoomsQuery("alice"), default);

        Assert.Equal([(RoomKind.Direct, "carol"), (RoomKind.Channel, "team")], rooms.Select(r => (r.Kind, r.Name)));
        Assert.All(rooms, r => Assert.Equal(1, r.UnreadCount));
    }

    [Fact]
    public async Task History_pages_backwards_from_the_newest_message()
    {
        for (var i = 1; i <= 5; i++)
        {
            await SendAsync(_team, "alice", $"m{i}");
        }

        var handler = new GetMessageHistoryHandler(_database.NewReadContext());
        var newest = await handler.HandleAsync(new GetMessageHistoryQuery(_team, "bob", Limit: 2), default);
        var older = await handler.HandleAsync(
            new GetMessageHistoryQuery(_team, "bob", Before: newest.Messages[0].Id, Limit: 2), default);
        var oldest = await handler.HandleAsync(
            new GetMessageHistoryQuery(_team, "bob", Before: older.Messages[0].Id, Limit: 2), default);

        Assert.Equal(["m4", "m5"], newest.Messages.Select(m => m.Body));
        Assert.True(newest.HasMore);
        Assert.Equal(["m2", "m3"], older.Messages.Select(m => m.Body));
        Assert.Equal(["m1"], oldest.Messages.Select(m => m.Body));
        Assert.False(oldest.HasMore);
    }

    [Fact]
    public async Task History_after_a_cursor_returns_what_was_missed_in_order()
    {
        var seen = await SendAsync(_team, "alice", "seen");
        await SendAsync(_team, "alice", "missed 1");
        await SendAsync(_team, "alice", "missed 2");

        var page = await new GetMessageHistoryHandler(_database.NewReadContext())
            .HandleAsync(new GetMessageHistoryQuery(_team, "bob", After: seen.Id), default);

        Assert.Equal(["missed 1", "missed 2"], page.Messages.Select(m => m.Body));
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task History_is_only_visible_to_members()
    {
        var handler = new GetMessageHistoryHandler(_database.NewReadContext());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.HandleAsync(new GetMessageHistoryQuery(_team, "carol"), default));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new GetMessageHistoryQuery(Guid.NewGuid(), "carol"), default));
    }

    [Theory]
    [InlineData(0L, 1L, 10)]
    [InlineData(null, null, 0)]
    [InlineData(null, null, 101)]
    public async Task History_rejects_bad_paging_arguments(long? before, long? after, int limit)
    {
        var handler = new GetMessageHistoryHandler(_database.NewReadContext());

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            handler.HandleAsync(new GetMessageHistoryQuery(_team, "alice", before, after, limit), default));
    }

    [Fact]
    public async Task Read_markers_list_every_member()
    {
        var message = await SendAsync(_team, "alice", "hello");
        await MarkReadAsync(_team, "bob", message.Id);

        var markers = await new GetReadMarkersHandler(_database.NewReadContext())
            .HandleAsync(new GetReadMarkersQuery(_team, "alice"), default);

        Assert.Equal([("alice", 0L), ("bob", message.Id)], markers.Select(m => (m.UserName, m.LastReadMessageId)));
    }

    [Fact]
    public async Task Channel_list_includes_seeded_channels_and_membership()
    {
        var channels = await new GetChannelsHandler(_database.NewReadContext())
            .HandleAsync(new GetChannelsQuery("bob"), default);

        Assert.Equal(["general", "random", "team"], channels.Select(c => c.Name));
        var team = channels.Single(c => c.Name == "team");
        Assert.Equal((2, true), (team.MemberCount, team.IsMember));
        Assert.False(channels.Single(c => c.Name == "general").IsMember);
    }

    [Fact]
    public async Task Users_are_listed_by_name()
    {
        var users = await new GetUsersHandler(_database.NewReadContext()).HandleAsync(new GetUsersQuery(), default);

        Assert.Equal(["alice", "bob", "carol"], users);
    }

    [Fact]
    public async Task The_read_context_refuses_to_save()
    {
        await using var read = _database.NewReadContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => read.SaveChangesAsync());
    }

    private async Task<MessageDto> SendAsync(Guid roomId, string sender, string body)
    {
        await using var db = _database.NewContext();
        return await new SendMessageHandler(db, _database.Clock)
            .HandleAsync(new SendMessageCommand(roomId, sender, body), default);
    }

    private async Task MarkReadAsync(Guid roomId, string user, long messageId)
    {
        await using var db = _database.NewContext();
        await new MarkReadHandler(db, _database.Clock).HandleAsync(new MarkReadCommand(roomId, user, messageId), default);
    }
}
