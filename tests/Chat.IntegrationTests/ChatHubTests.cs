using System.Net;
using System.Net.Http.Json;
using Chat.Api.Realtime;
using Chat.Application.Messages;
using Chat.Application.Messages.Queries;
using Chat.Application.Rooms.Queries;
using Chat.Domain.Rooms;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chat.IntegrationTests;

public sealed class ChatHubTests : IAsyncLifetime
{
    private static readonly Guid General = new("6f1c2a9e-0c6b-4d8e-9a51-6b1a3c2e7f01");

    private readonly ChatApiFactory _factory = new();
    private readonly List<HubConnection> _connections = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }

        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task A_message_sent_through_the_hub_reaches_other_members_of_the_room()
    {
        var alice = await _factory.SignInAsync("alice");
        var bob = await _factory.SignInAsync("bob");
        await alice.PostAsync<RoomSummaryDto>($"/api/channels/{General}/join", new { });
        await bob.PostAsync<RoomSummaryDto>($"/api/channels/{General}/join", new { });

        var aliceHub = await ConnectAsync(alice);
        var bobHub = await ConnectAsync(bob);
        var bobMessages = new HubEvents<MessageDto>(bobHub, nameof(IChatClient.MessageReceived));

        var sent = await aliceHub.InvokeAsync<MessageDto>("SendMessage", General, "hello bob");
        var received = await bobMessages.NextAsync();

        Assert.Equal(sent, received);
        Assert.Equal(("alice", "hello bob"), (received.Sender, received.Body));
    }

    [Fact]
    public async Task Messages_posted_over_http_are_pushed_to_connected_clients()
    {
        var alice = await _factory.SignInAsync("alice");
        var bob = await _factory.SignInAsync("bob");
        await alice.PostAsync<RoomSummaryDto>($"/api/channels/{General}/join", new { });
        await bob.PostAsync<RoomSummaryDto>($"/api/channels/{General}/join", new { });

        var bobHub = await ConnectAsync(bob);
        var bobMessages = new HubEvents<MessageDto>(bobHub, nameof(IChatClient.MessageReceived));

        var sent = await alice.PostAsync<MessageDto>($"/api/rooms/{General}/messages", new { body = "via http" });

        Assert.Equal(sent.Id, (await bobMessages.NextAsync()).Id);
    }

    [Fact]
    public async Task Opening_a_direct_conversation_adds_the_room_to_the_other_users_live_connection()
    {
        var alice = await _factory.SignInAsync("alice");
        var bob = await _factory.SignInAsync("bob");
        var aliceHub = await ConnectAsync(alice);
        var bobHub = await ConnectAsync(bob);
        var bobRooms = new HubEvents<RoomSummaryDto>(bobHub, nameof(IChatClient.RoomAdded));
        var bobMessages = new HubEvents<MessageDto>(bobHub, nameof(IChatClient.MessageReceived));

        var direct = await alice.PostAsync<RoomSummaryDto>("/api/direct", new { userName = "bob" });
        var added = await bobRooms.NextAsync();
        await aliceHub.InvokeAsync<MessageDto>("SendMessage", direct.Id, "psst");

        Assert.Equal((direct.Id, RoomKind.Direct, "alice"), (added.Id, added.Kind, added.Name));
        Assert.Equal("bob", direct.Name);
        Assert.Equal("psst", (await bobMessages.NextAsync()).Body);
    }

    [Fact]
    public async Task Read_receipts_are_broadcast_to_the_room()
    {
        var alice = await _factory.SignInAsync("alice");
        var bob = await _factory.SignInAsync("bob");
        var room = await alice.PostAsync<RoomSummaryDto>("/api/direct", new { userName = "bob" });
        var aliceHub = await ConnectAsync(alice);
        var bobHub = await ConnectAsync(bob);
        var aliceReceipts = new HubEvents<ReadReceiptDto>(aliceHub, nameof(IChatClient.ReadReceipt));

        var message = await aliceHub.InvokeAsync<MessageDto>("SendMessage", room.Id, "did you see this?");
        Assert.True(await bobHub.InvokeAsync<bool>("MarkRead", room.Id, message.Id));
        var receipt = await aliceReceipts.NextAsync();

        Assert.Equal((room.Id, "bob", message.Id), (receipt.RoomId, receipt.UserName, receipt.LastReadMessageId));
        var unread = await bob.GetEventuallyAsync<List<UnreadCountDto>>(
            "/api/rooms/unread", counts => counts.Count == 1 && counts[0].LastReadMessageId == message.Id);
        Assert.Equal(0, Assert.Single(unread).UnreadCount);
    }

    [Fact]
    public async Task Presence_follows_the_last_open_connection()
    {
        var alice = await _factory.SignInAsync("alice");
        var bob = await _factory.SignInAsync("bob");
        var aliceHub = await ConnectAsync(alice);
        var presence = new HubEvents<PresenceDto>(aliceHub, nameof(IChatClient.PresenceChanged));

        var bobPhone = await ConnectAsync(bob);
        Assert.Equal(new PresenceDto("bob", true), await presence.NextAsync());

        var bobLaptop = await ConnectAsync(bob);
        await bobPhone.StopAsync();
        Assert.Equal(["alice", "bob"], await aliceHub.InvokeAsync<List<string>>("GetOnlineUsers"));

        await bobLaptop.StopAsync();
        Assert.Equal(new PresenceDto("bob", false), await presence.NextAsync());
        Assert.Equal(["alice"], await alice.GetAsync<List<string>>("/api/presence"));
    }

    [Fact]
    public async Task Typing_is_throttled_and_carries_an_expiry()
    {
        var alice = await _factory.SignInAsync("alice");
        var bob = await _factory.SignInAsync("bob");
        var room = await alice.PostAsync<RoomSummaryDto>("/api/direct", new { userName = "bob" });
        var aliceHub = await ConnectAsync(alice);
        var bobHub = await ConnectAsync(bob);
        var bobTyping = new HubEvents<TypingDto>(bobHub, nameof(IChatClient.UserTyping));

        for (var i = 0; i < 5; i++)
        {
            await aliceHub.InvokeAsync("StartTyping", room.Id);
        }

        Assert.Equal(new TypingDto(room.Id, "alice", 5000), await bobTyping.NextAsync());
        Assert.True(await bobTyping.NoneWithinAsync(TimeSpan.FromMilliseconds(500)));

        // Sending ends the window, so the next keystroke is announced immediately.
        await aliceHub.InvokeAsync<MessageDto>("SendMessage", room.Id, "done typing");
        await aliceHub.InvokeAsync("StartTyping", room.Id);
        Assert.Equal("alice", (await bobTyping.NextAsync()).UserName);
    }

    [Fact]
    public async Task Hub_errors_explain_what_went_wrong()
    {
        var mallory = await _factory.SignInAsync("mallory");
        var hub = await ConnectAsync(mallory);

        var error = await Assert.ThrowsAsync<HubException>(() =>
            hub.InvokeAsync<MessageDto>("SendMessage", General, "not a member"));

        Assert.Contains("Join the room before posting", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connecting_without_a_token_is_rejected()
    {
        var connection = _factory.CreateHubConnection(accessToken: null);
        _connections.Add(connection);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task History_and_unread_counts_reflect_messages_sent_over_the_hub()
    {
        var alice = await _factory.SignInAsync("alice");
        var bob = await _factory.SignInAsync("bob");
        var room = await alice.PostAsync<RoomSummaryDto>("/api/direct", new { userName = "bob" });
        var aliceHub = await ConnectAsync(alice);

        for (var i = 1; i <= 3; i++)
        {
            await aliceHub.InvokeAsync<MessageDto>("SendMessage", room.Id, $"message {i}");
        }

        // Queries read the read model, so wait until the projection has applied all three.
        var bobRooms = await bob.GetEventuallyAsync<List<RoomSummaryDto>>(
            "/api/rooms", rooms => rooms.Count == 1 && rooms[0].UnreadCount == 3);
        var latest = await bob.GetAsync<MessagePage>($"/api/rooms/{room.Id}/messages?limit=2");
        var older = await bob.GetAsync<MessagePage>($"/api/rooms/{room.Id}/messages?limit=2&before={latest.Messages[0].Id}");

        Assert.Equal(["message 2", "message 3"], latest.Messages.Select(m => m.Body));
        Assert.True(latest.HasMore);
        Assert.Equal(["message 1"], older.Messages.Select(m => m.Body));
        Assert.Equal(3, Assert.Single(bobRooms).UnreadCount);
    }

    [Fact]
    public async Task Edits_and_deletions_are_pushed_to_the_room()
    {
        var alice = await _factory.SignInAsync("alice");
        var bob = await _factory.SignInAsync("bob");
        var room = await alice.PostAsync<RoomSummaryDto>("/api/direct", new { userName = "bob" });
        var aliceHub = await ConnectAsync(alice);
        var bobHub = await ConnectAsync(bob);
        var bobEdits = new HubEvents<MessageDto>(bobHub, nameof(IChatClient.MessageEdited));
        var bobDeletions = new HubEvents<MessageDeletedDto>(bobHub, nameof(IChatClient.MessageDeleted));

        var sent = await aliceHub.InvokeAsync<MessageDto>("SendMessage", room.Id, "teh plan");
        var edited = await aliceHub.InvokeAsync<MessageDto>("EditMessage", room.Id, sent.Id, "the plan");
        var pushed = await bobEdits.NextAsync();

        Assert.Equal((sent.Id, "the plan", 2), (pushed.Id, pushed.Body, pushed.Revision));
        Assert.Equal(edited.EditedAt, pushed.EditedAt);

        var deleted = await alice.Http.DeleteAsync($"/api/rooms/{room.Id}/messages/{sent.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(sent.Id, (await bobDeletions.NextAsync()).MessageId);
    }

    [Fact]
    public async Task Only_the_author_may_edit_a_message()
    {
        var alice = await _factory.SignInAsync("alice");
        var bob = await _factory.SignInAsync("bob");
        var room = await alice.PostAsync<RoomSummaryDto>("/api/direct", new { userName = "bob" });
        var sent = await alice.PostAsync<MessageDto>($"/api/rooms/{room.Id}/messages", new { body = "mine" });

        var response = await bob.Http.PutAsJsonAsync($"/api/rooms/{room.Id}/messages/{sent.Id}", new { body = "ours" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HubConnection> ConnectAsync(TestUser user)
    {
        var connection = await user.ConnectAsync();
        _connections.Add(connection);
        return connection;
    }
}
