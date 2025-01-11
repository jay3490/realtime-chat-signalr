using Chat.Application.Common;
using Chat.Application.Rooms;
using Chat.Application.Users;
using Chat.Domain.Rooms;
using Chat.UnitTests.Support;
using Microsoft.EntityFrameworkCore;

namespace Chat.UnitTests.Commands;

public sealed class RoomCommandTests : IAsyncLifetime
{
    private SqliteChatDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _database = await SqliteChatDatabase.CreateAsync();
        await _database.RegisterUsersAsync("alice", "bob");
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Channel_names_are_unique_regardless_of_case()
    {
        await CreateChannelAsync("Design", "alice");

        await Assert.ThrowsAsync<ConflictException>(() => CreateChannelAsync("design", "bob"));
    }

    [Fact]
    public async Task Creating_a_channel_raises_room_created_for_the_creator()
    {
        var id = await CreateChannelAsync("design", "alice");

        var created = Assert.IsType<RoomCreated>(Assert.Single(_database.Events.Dispatched));
        Assert.Equal(id, created.RoomId);
        Assert.Equal(["alice"], created.Members);
    }

    [Fact]
    public async Task Joining_adds_the_member_once()
    {
        var id = await CreateChannelAsync("design", "alice");
        _database.Events.Clear();

        await JoinAsync(id, "bob");
        await JoinAsync(id, "bob");

        Assert.IsType<MemberJoined>(Assert.Single(_database.Events.Dispatched));
        await using var db = _database.NewContext();
        Assert.Equal(2, await db.Set<RoomMember>().CountAsync(m => m.RoomId == id));
    }

    [Fact]
    public async Task Direct_conversations_cannot_be_joined_by_id()
    {
        var direct = await OpenDirectAsync("alice", "bob");

        await Assert.ThrowsAsync<NotFoundException>(() => JoinAsync(direct, "mallory"));
    }

    [Fact]
    public async Task Opening_a_direct_conversation_from_either_side_returns_the_same_room()
    {
        var fromAlice = await OpenDirectAsync("alice", "bob");
        var fromBob = await OpenDirectAsync("bob", "ALICE");

        Assert.Equal(fromAlice, fromBob);
        var created = Assert.IsType<RoomCreated>(Assert.Single(_database.Events.Dispatched));
        Assert.Equal(["alice", "bob"], created.Members);
    }

    [Fact]
    public async Task Direct_conversations_need_a_known_user()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => OpenDirectAsync("alice", "nobody"));
    }

    [Fact]
    public async Task Signing_in_registers_new_users_and_normalizes_the_name()
    {
        await using (var db = _database.NewContext())
        {
            var name = await new SignInHandler(db, _database.Clock).HandleAsync(new SignInCommand(" Carol "), default);
            Assert.Equal("carol", name);
        }

        await using (var db = _database.NewContext())
        {
            _database.Clock.Advance(TimeSpan.FromMinutes(1));
            await new SignInHandler(db, _database.Clock).HandleAsync(new SignInCommand("carol"), default);
        }

        await using var verify = _database.NewContext();
        var carol = await verify.Users.SingleAsync(u => u.Name == "carol");
        Assert.Equal(TimeSpan.FromMinutes(1), carol.LastSeenAt - carol.FirstSeenAt);
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

    private async Task<Guid> OpenDirectAsync(string user, string other)
    {
        await using var db = _database.NewContext();
        return await new OpenDirectConversationHandler(db, _database.Clock)
            .HandleAsync(new OpenDirectConversationCommand(user, other), default);
    }
}
