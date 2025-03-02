using Chat.Application.Common;
using Chat.Application.ReadModel;
using Chat.Application.Sync;
using Chat.Contracts;
using Chat.Infrastructure.ReadModel;
using Chat.UnitTests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chat.UnitTests.Sync;

public sealed class SyncTests : IAsyncLifetime
{
    private static readonly Guid Team = new("3a2b1c0d-9e8f-4a7b-8c6d-5e4f3a2b1c0d");
    private static readonly Guid Secret = new("4b3c2d1e-0f9a-4b8c-9d7e-6f5a4b3c2d1e");
    private static readonly DateTimeOffset At = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private SqliteReadModel _readModel = null!;

    public async Task InitializeAsync()
    {
        _readModel = await SqliteReadModel.CreateAsync();
        await _readModel.ProjectAsync(
            new RoomCreated(Team, "Channel", "team", ["alice", "bob"], "alice", At),
            new RoomCreated(Secret, "Direct", null, ["alice", "carol"], "alice", At));
    }

    public async Task DisposeAsync() => await _readModel.DisposeAsync();

    [Fact]
    public void Cursors_round_trip_and_reject_garbage()
    {
        var cursor = new SyncCursor(Guid.NewGuid(), 42);

        Assert.True(SyncCursor.TryParse(cursor.ToString(), out var parsed));
        Assert.Equal(cursor, parsed);
        Assert.False(SyncCursor.TryParse("42", out _));
        Assert.False(SyncCursor.TryParse($"{Guid.NewGuid():N}.-1", out _));
        Assert.False(SyncCursor.TryParse(null, out _));
    }

    [Fact]
    public async Task Without_a_cursor_a_device_gets_the_head_and_nothing_else()
    {
        var page = await ChangesAsync("bob", cursor: null);

        Assert.Empty(page.Changes);
        Assert.False(page.Reset);
        Assert.Equal(2, Position(page.Cursor));
    }

    [Fact]
    public async Task A_device_gets_changes_in_its_rooms_since_its_cursor_in_order()
    {
        var head = (await ChangesAsync("bob", cursor: null)).Cursor;
        await _readModel.ProjectAsync(
            new MessageSent(Team, 1, "alice", "for the team", [], At),
            new MessageSent(Secret, 2, "alice", "for carol only", [], At),
            new MessageEdited(Team, 1, "alice", "for the whole team", 2, At),
            new ReadReceiptRecorded(Team, "bob", 1, At));

        var page = await ChangesAsync("bob", head);

        Assert.Equal(
            [SyncChangeKinds.MessageSent, SyncChangeKinds.MessageEdited, SyncChangeKinds.ReadReceipt],
            page.Changes.Select(c => c.Kind));
        Assert.All(page.Changes, c => Assert.Equal(Team, c.RoomId));

        // Message changes carry the message as it is now, so replaying converges on it.
        Assert.Equal("for the whole team", page.Changes[0].Message!.Body);
        Assert.Equal(("bob", 1L), (page.Changes[2].UserName, page.Changes[2].MessageId));
        Assert.Null(page.Changes[2].Message);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task Two_devices_with_different_cursors_converge()
    {
        var phone = (await ChangesAsync("bob", cursor: null)).Cursor;
        await _readModel.ProjectAsync(new MessageSent(Team, 1, "alice", "one", [], At));
        var laptop = (await ChangesAsync("bob", cursor: null)).Cursor;
        await _readModel.ProjectAsync(new MessageSent(Team, 2, "alice", "two", [], At));

        var phonePage = await ChangesAsync("bob", phone);
        var laptopPage = await ChangesAsync("bob", laptop);

        Assert.Equal([1L, 2L], phonePage.Changes.Select(c => c.MessageId!.Value));
        Assert.Equal([2L], laptopPage.Changes.Select(c => c.MessageId!.Value));
        Assert.Equal(phonePage.Cursor, laptopPage.Cursor);
    }

    [Fact]
    public async Task Changes_come_in_pages()
    {
        var head = (await ChangesAsync("bob", cursor: null)).Cursor;
        for (var id = 1; id <= 5; id++)
        {
            await _readModel.ProjectAsync(new MessageSent(Team, id, "alice", $"m{id}", [], At));
        }

        var first = await ChangesAsync("bob", head, limit: 3);
        var second = await ChangesAsync("bob", first.Cursor, limit: 3);

        Assert.True(first.HasMore);
        Assert.Equal([1L, 2L, 3L], first.Changes.Select(c => c.MessageId!.Value));
        Assert.False(second.HasMore);
        Assert.Equal([4L, 5L], second.Changes.Select(c => c.MessageId!.Value));
    }

    [Fact]
    public async Task A_cursor_from_before_a_rebuild_asks_the_device_to_reload()
    {
        var old = (await ChangesAsync("bob", cursor: null)).Cursor;
        await using (var db = _readModel.NewContext())
        {
            await new ReadModelRebuilder(db, NullLogger<ReadModelRebuilder>.Instance).ResetAsync(default);
        }

        var page = await ChangesAsync("bob", old);

        Assert.True(page.Reset);
        Assert.Empty(page.Changes);
        Assert.NotEqual(old, page.Cursor);
    }

    [Fact]
    public async Task A_cursor_beyond_the_head_is_treated_as_unknown()
    {
        var head = (await ChangesAsync("bob", cursor: null)).Cursor;
        Assert.True(SyncCursor.TryParse(head, out var parsed));

        var page = await ChangesAsync("bob", (parsed!.Value with { Position = 1000 }).ToString());

        Assert.True(page.Reset);
        Assert.Equal(head, page.Cursor);
    }

    [Fact]
    public async Task A_malformed_cursor_is_a_client_error()
    {
        await Assert.ThrowsAsync<RequestValidationException>(() => ChangesAsync("bob", "not-a-cursor"));
    }

    private async Task<SyncPage> ChangesAsync(string user, string? cursor, int limit = GetChangesQuery.DefaultLimit) =>
        await new GetChangesHandler(_readModel.NewQueryContext()).HandleAsync(new GetChangesQuery(user, cursor, limit), default);

    private static long Position(string cursor) =>
        SyncCursor.TryParse(cursor, out var parsed) ? parsed.Value.Position : throw new FormatException(cursor);
}
