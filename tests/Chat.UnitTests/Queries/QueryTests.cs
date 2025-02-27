using Chat.Application.Common;
using Chat.Application.Messages;
using Chat.Application.Messages.Queries;
using Chat.Application.Rooms;
using Chat.Application.Rooms.Queries;
using Chat.Application.Users.Queries;
using Chat.Domain.Rooms;
using Chat.UnitTests.Support;

namespace Chat.UnitTests.Queries;

/// <summary>
/// Commands run against the write database; their outbox is then projected into a separate
/// read database, and the query handlers read that, as they do in the API.
/// </summary>
public sealed class QueryTests : IAsyncLifetime
{
    private SqliteChatDatabase _database = null!;
    private SqliteReadModel _readModel = null!;
    private Guid _team;

    public async Task InitializeAsync()
    {
        _database = await SqliteChatDatabase.CreateAsync();
        _readModel = await SqliteReadModel.CreateAsync();
        await _database.RegisterUsersAsync("alice", "bob", "carol");

        await using var db = _database.NewContext();
        _team = await new CreateChannelHandler(db, _database.Clock).HandleAsync(new CreateChannelCommand("team", "alice"), default);
        await new JoinChannelHandler(db, _database.Clock).HandleAsync(new JoinChannelCommand(_team, "bob"), default);
    }

    public async Task DisposeAsync()
    {
        await _readModel.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task Unread_counts_exclude_own_messages_and_anything_already_read()
    {
        await SendAsync(_team, "alice", "one");
        var two = await SendAsync(_team, "alice", "two");
        await SendAsync(_team, "alice", "three");
        await SendAsync(_team, "bob", "bob's own message");
        await MarkReadAsync(_team, "bob", two.Id);
        await _readModel.CatchUpAsync(_database);

        var bobRooms = await new RoomSummaryHandlers(_readModel.NewQueryContext())
            .HandleAsync(new GetMyRoomsQuery("bob"), default);
        var counts = await new GetUnreadCountsHandler(_readModel.NewQueryContext())
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
        await _readModel.CatchUpAsync(_database);

        var rooms = await new RoomSummaryHandlers(_readModel.NewQueryContext())
            .HandleAsync(new GetMyRoomsQuery("alice"), default);

        Assert.Equal([(RoomKind.Direct, "carol"), (RoomKind.Channel, "team")], rooms.Select(r => (r.Kind, r.Name)));
        Assert.All(rooms, r => Assert.Equal(1, r.UnreadCount));
    }

    [Fact]
    public async Task The_current_summary_reflects_a_join_before_the_projection_catches_up()
    {
        await SendAsync(_team, "alice", "before carol");
        await using (var db = _database.NewContext())
        {
            await new JoinChannelHandler(db, _database.Clock).HandleAsync(new JoinChannelCommand(_team, "carol"), default);
        }

        await using var write = _database.NewContext();
        var summary = await new CurrentRoomSummaryHandler(write).HandleAsync(new GetCurrentRoomSummaryQuery(_team, "carol"), default);
        await Assert.ThrowsAsync<NotFoundException>(() => new RoomSummaryHandlers(_readModel.NewQueryContext())
            .HandleAsync(new GetRoomSummaryQuery(_team, "carol"), default));

        Assert.Equal(("team", 1, 0L), (summary.Name, summary.UnreadCount, summary.LastReadMessageId));
    }

    [Fact]
    public async Task History_pages_backwards_from_the_newest_message()
    {
        for (var i = 1; i <= 5; i++)
        {
            await SendAsync(_team, "alice", $"m{i}");
        }

        await _readModel.CatchUpAsync(_database);

        var newest = await HistoryAsync(new GetMessageHistoryQuery(_team, "bob", Limit: 2));
        var older = await HistoryAsync(new GetMessageHistoryQuery(_team, "bob", Before: newest.Messages[0].Id, Limit: 2));
        var oldest = await HistoryAsync(new GetMessageHistoryQuery(_team, "bob", Before: older.Messages[0].Id, Limit: 2));

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
        await _readModel.CatchUpAsync(_database);

        var page = await HistoryAsync(new GetMessageHistoryQuery(_team, "bob", After: seen.Id));

        Assert.Equal(["missed 1", "missed 2"], page.Messages.Select(m => m.Body));
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task History_shows_edits_and_deletions()
    {
        var kept = await SendAsync(_team, "alice", "frist");
        var removed = await SendAsync(_team, "alice", "oops");
        await using (var db = _database.NewContext())
        {
            var changes = new MessageChangeHandlers(db, _database.Clock);
            await changes.HandleAsync(new EditMessageCommand(_team, kept.Id, "alice", "first"), default);
            await changes.HandleAsync(new DeleteMessageCommand(_team, removed.Id, "alice"), default);
        }

        await _readModel.CatchUpAsync(_database);
        var page = await HistoryAsync(new GetMessageHistoryQuery(_team, "bob"));
        await using var write = _database.NewContext();
        var revisions = await new GetMessageRevisionsHandler(write, _readModel.NewQueryContext())
            .HandleAsync(new GetMessageRevisionsQuery(_team, kept.Id, "bob"), default);

        Assert.Equal([("first", 2, false), (string.Empty, 1, true)], page.Messages.Select(m => (m.Body, m.Revision, m.IsDeleted)));
        Assert.Equal(["frist", "first"], revisions.Select(r => r.Body));
    }

    [Fact]
    public async Task History_is_only_visible_to_members()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => HistoryAsync(new GetMessageHistoryQuery(_team, "carol")));
        await Assert.ThrowsAsync<NotFoundException>(() => HistoryAsync(new GetMessageHistoryQuery(Guid.NewGuid(), "carol")));
    }

    [Theory]
    [InlineData(0L, 1L, 10)]
    [InlineData(null, null, 0)]
    [InlineData(null, null, 101)]
    public async Task History_rejects_bad_paging_arguments(long? before, long? after, int limit)
    {
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            HistoryAsync(new GetMessageHistoryQuery(_team, "alice", before, after, limit)));
    }

    [Fact]
    public async Task Read_markers_list_every_member()
    {
        var message = await SendAsync(_team, "alice", "hello");
        await MarkReadAsync(_team, "bob", message.Id);
        await _readModel.CatchUpAsync(_database);

        await using var write = _database.NewContext();
        var markers = await new GetReadMarkersHandler(write, _readModel.NewQueryContext())
            .HandleAsync(new GetReadMarkersQuery(_team, "alice"), default);

        Assert.Equal([("alice", 0L), ("bob", message.Id)], markers.Select(m => (m.UserName, m.LastReadMessageId)));
    }

    [Fact]
    public async Task Channel_list_includes_seeded_channels_and_membership()
    {
        await _readModel.CatchUpAsync(_database);

        var channels = await new GetChannelsHandler(_readModel.NewQueryContext())
            .HandleAsync(new GetChannelsQuery("bob"), default);

        Assert.Equal(["general", "random", "team"], channels.Select(c => c.Name));
        var team = channels.Single(c => c.Name == "team");
        Assert.Equal((2, true), (team.MemberCount, team.IsMember));
        Assert.False(channels.Single(c => c.Name == "general").IsMember);
    }

    [Fact]
    public async Task Users_are_listed_by_name()
    {
        await _readModel.CatchUpAsync(_database);

        var users = await new GetUsersHandler(_readModel.NewQueryContext()).HandleAsync(new GetUsersQuery(), default);

        Assert.Equal(["alice", "bob", "carol"], users);
    }

    [Fact]
    public async Task Membership_checks_read_the_write_model_so_a_join_counts_at_once()
    {
        await using (var db = _database.NewContext())
        {
            await new JoinChannelHandler(db, _database.Clock).HandleAsync(new JoinChannelCommand(_team, "carol"), default);
        }

        await using var write = _database.NewContext();
        var handlers = new RoomMembershipHandlers(write);

        Assert.True(await handlers.HandleAsync(new IsRoomMemberQuery(_team, "carol"), default));
        Assert.Contains(_team, await handlers.HandleAsync(new GetMyRoomIdsQuery("carol"), default));
    }

    [Fact]
    public async Task The_read_context_refuses_to_save()
    {
        await using var read = _readModel.NewQueryContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => read.SaveChangesAsync());
    }

    private async Task<MessagePage> HistoryAsync(GetMessageHistoryQuery query)
    {
        await using var write = _database.NewContext();
        return await new GetMessageHistoryHandler(write, _readModel.NewQueryContext()).HandleAsync(query, default);
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
