using System.Text.Json;
using Chat.Contracts;
using Chat.Infrastructure.ReadModel;
using Chat.UnitTests.Support;
using Microsoft.EntityFrameworkCore;

namespace Chat.UnitTests.Projections;

public sealed class ReadModelProjectorTests : IAsyncLifetime
{
    private static readonly Guid Room = new("2c1d7f0e-8a3b-4c5d-9e6f-0a1b2c3d4e5f");
    private static readonly DateTimeOffset At = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private SqliteReadModel _readModel = null!;

    public async Task InitializeAsync()
    {
        _readModel = await SqliteReadModel.CreateAsync();
        await _readModel.ProjectAsync(
            new UserRegistered("alice", At),
            new UserRegistered("bob", At),
            new RoomCreated(Room, "Channel", "team", ["alice"], "alice", At),
            new MemberJoined(Room, "bob", At));
    }

    public async Task DisposeAsync() => await _readModel.DisposeAsync();

    [Fact]
    public async Task Rooms_members_and_users_are_built_from_events()
    {
        await using var db = _readModel.NewContext();

        var room = await db.Rooms.SingleAsync(r => r.Id == Room);
        Assert.Equal(("team", 2), (room.Name, room.MemberCount));
        Assert.Equal(["alice", "bob"], await db.Members.Where(m => m.RoomId == Room).OrderBy(m => m.UserName).Select(m => m.UserName).ToListAsync());
        Assert.Equal(["alice", "bob"], await db.Users.OrderBy(u => u.Name).Select(u => u.Name).ToListAsync());
    }

    [Fact]
    public async Task Messages_raise_unread_counts_for_everyone_but_the_sender()
    {
        await _readModel.ProjectAsync(Sent(1, "alice"), Sent(2, "alice"), Sent(3, "bob"));

        Assert.Equal((1, 2), await UnreadAsync());
        await using var db = _readModel.NewContext();
        Assert.Equal(3, (await db.Rooms.SingleAsync(r => r.Id == Room)).LastMessageId);
    }

    [Fact]
    public async Task A_read_receipt_recounts_what_is_still_unread_and_never_moves_back()
    {
        await _readModel.ProjectAsync(Sent(1, "alice"), Sent(2, "alice"), Sent(3, "alice"));

        await _readModel.ProjectAsync(new ReadReceiptRecorded(Room, "bob", 2, At));
        Assert.Equal(1, (await UnreadAsync()).Bob);

        await _readModel.ProjectAsync(new ReadReceiptRecorded(Room, "bob", 1, At));
        await using var db = _readModel.NewContext();
        Assert.Equal(2, (await db.Members.SingleAsync(m => m.UserName == "bob")).LastReadMessageId);
    }

    [Fact]
    public async Task Edits_keep_every_revision_and_ignore_stale_ones()
    {
        await _readModel.ProjectAsync(
            Sent(1, "alice", "v1"),
            new MessageEdited(Room, 1, "alice", "v3", 3, At.AddMinutes(2)),
            new MessageEdited(Room, 1, "alice", "v2", 2, At.AddMinutes(1)));

        await using var db = _readModel.NewContext();
        var message = await db.Messages.SingleAsync();
        Assert.Equal(("v3", 3), (message.Body, message.Revision));
        Assert.Equal(["v1", "v3"], await db.MessageRevisions.OrderBy(r => r.Revision).Select(r => r.Body).ToListAsync());
    }

    [Fact]
    public async Task Deleting_leaves_a_tombstone_drops_its_history_and_its_unread_count()
    {
        await _readModel.ProjectAsync(Sent(1, "alice"), Sent(2, "alice"), new MessageEdited(Room, 2, "alice", "fixed", 2, At));

        await _readModel.ProjectAsync(new MessageDeleted(Room, 2, "alice", At), new MessageDeleted(Room, 2, "alice", At));

        Assert.Equal(1, (await UnreadAsync()).Bob);
        await using var db = _readModel.NewContext();
        var tombstone = await db.Messages.SingleAsync(m => m.Id == 2);
        Assert.Equal((string.Empty, At), (tombstone.Body, tombstone.DeletedAt));
        Assert.Empty(await db.MessageRevisions.Where(r => r.MessageId == 2).ToListAsync());
    }

    [Fact]
    public async Task A_late_joiner_starts_with_the_existing_messages_unread()
    {
        await _readModel.ProjectAsync(Sent(1, "alice"), Sent(2, "bob"), new MemberJoined(Room, "carol", At));

        await using var db = _readModel.NewContext();
        Assert.Equal(2, (await db.Members.SingleAsync(m => m.UserName == "carol")).UnreadCount);
    }

    [Fact]
    public async Task Replaying_an_offset_at_or_behind_the_checkpoint_changes_nothing()
    {
        await _readModel.ProjectAsync(Sent(1, "alice"));
        var sent = _readModel.Log[^1];

        Assert.Equal(ProjectionOutcome.AlreadyProjected, await _readModel.ProjectAsync(sent));
        Assert.Equal(1, (await UnreadAsync()).Bob);
    }

    [Fact]
    public async Task A_resent_copy_of_an_event_at_a_new_offset_is_recognized_by_id()
    {
        await _readModel.ProjectAsync(Sent(1, "alice"));
        var copy = _readModel.Append(_readModel.Log[^1].Envelope!);

        Assert.Equal(ProjectionOutcome.Duplicate, await _readModel.ProjectAsync(copy));
        Assert.Equal(1, (await UnreadAsync()).Bob);

        await using var db = _readModel.NewContext();
        var projector = _readModel.NewProjector(db);
        Assert.Equal(copy.Offset + 1, await projector.ResumeOffsetAsync(EventStreams.Rooms, 0, default));
    }

    [Fact]
    public async Task An_unreadable_event_is_skipped_without_blocking_the_partition()
    {
        var broken = new ConsumedEvent(EventStreams.Rooms, 0, 100, null, "not JSON");

        Assert.Equal(ProjectionOutcome.Unreadable, await _readModel.ProjectAsync(broken));

        await using var db = _readModel.NewContext();
        Assert.Equal(101, await _readModel.NewProjector(db).ResumeOffsetAsync(EventStreams.Rooms, 0, default));
    }

    [Fact]
    public async Task Rebuilding_from_the_log_reproduces_the_same_read_model()
    {
        await _readModel.ProjectAsync(
            Sent(1, "alice", "hello"),
            Sent(2, "bob", "hi"),
            new MessageEdited(Room, 1, "alice", "hello!", 2, At),
            new ReadReceiptRecorded(Room, "bob", 1, At),
            new MessageDeleted(Room, 2, "bob", At),
            new MemberJoined(Room, "carol", At));
        var before = await SnapshotAsync();

        await using (var db = _readModel.NewContext())
        {
            await new ReadModelRebuilder(db).ResetAsync(default);
            Assert.Null(await _readModel.NewProjector(db).ResumeOffsetAsync(EventStreams.Rooms, 0, default));
            Assert.Equal(2, await db.Rooms.CountAsync());
        }

        foreach (var consumed in _readModel.Log.ToList())
        {
            Assert.Equal(ProjectionOutcome.Applied, await _readModel.ProjectAsync(consumed));
        }

        Assert.Equal(before, await SnapshotAsync());
    }

    private static MessageSent Sent(long id, string sender, string body = "text") =>
        new(Room, id, sender, body, [], At.AddSeconds(id));

    private async Task<(int Alice, int Bob)> UnreadAsync()
    {
        await using var db = _readModel.NewContext();
        var members = await db.Members.Where(m => m.RoomId == Room).ToDictionaryAsync(m => m.UserName, m => m.UnreadCount);
        return (members["alice"], members["bob"]);
    }

    private async Task<string> SnapshotAsync()
    {
        await using var db = _readModel.NewContext();
        return JsonSerializer.Serialize(new
        {
            rooms = await db.Rooms.OrderBy(r => r.Id).ToListAsync(),
            members = await db.Members.OrderBy(m => m.RoomId).ThenBy(m => m.UserName).ToListAsync(),
            messages = await db.Messages.OrderBy(m => m.Id).ToListAsync(),
            revisions = await db.MessageRevisions.OrderBy(r => r.MessageId).ThenBy(r => r.Revision).ToListAsync(),
            users = await db.Users.OrderBy(u => u.Name).ToListAsync(),
            checkpoints = await db.Checkpoints.OrderBy(c => c.Stream).ToListAsync(),
        });
    }
}
