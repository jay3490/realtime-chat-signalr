using Chat.Api.Realtime;
using Chat.Application.Messages;
using Chat.Application.Rooms.Queries;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;

namespace Chat.IntegrationTests;

/// <summary>
/// Two API instances sharing one database and one Redis, like the compose replicas. Each
/// user is connected to a different instance, so every event below has to cross the Redis
/// backplane. Skipped unless <c>CHAT_TEST_REDIS</c> points at a Redis server.
/// </summary>
public sealed class BackplaneTests : IAsyncLifetime
{
    private const string EnvironmentVariable = "CHAT_TEST_REDIS";

    private readonly string _databasePath = ChatApiFactory.NewDatabasePath();
    private readonly string _prefix = $"chat-test:{Guid.NewGuid():N}:";
    private readonly List<HubConnection> _connections = [];
    private ChatApiFactory? _first;
    private ChatApiFactory? _second;

    public Task InitializeAsync()
    {
        var redis = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(redis))
        {
            _first = new ChatApiFactory(_databasePath, redis, _prefix);
            _second = new ChatApiFactory(_databasePath, redis, _prefix);
        }

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }

        if (_first is not null)
        {
            await _first.DisposeAsync();
        }

        if (_second is not null)
        {
            await _second.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [SkippableFact]
    public async Task Events_reach_a_user_connected_to_the_other_instance()
    {
        Skip.If(_first is null || _second is null, $"Set {EnvironmentVariable} to run the backplane tests.");

        // Starting the first host creates the schema before the second one checks for it.
        var alice = await _first!.SignInAsync("alice");
        var bob = await _second!.SignInAsync("bob");
        var room = await alice.PostAsync<RoomSummaryDto>("/api/direct", new { userName = "bob" });

        var aliceHub = await ConnectAsync(alice);
        var bobHub = await ConnectAsync(bob);
        var bobTyping = new HubEvents<TypingDto>(bobHub, nameof(IChatClient.UserTyping));
        var bobMessages = new HubEvents<MessageDto>(bobHub, nameof(IChatClient.MessageReceived));
        var aliceReceipts = new HubEvents<ReadReceiptDto>(aliceHub, nameof(IChatClient.ReadReceipt));

        await aliceHub.InvokeAsync("StartTyping", room.Id);
        Assert.Equal("alice", (await bobTyping.NextAsync()).UserName);

        var sent = await aliceHub.InvokeAsync<MessageDto>("SendMessage", room.Id, "across instances");
        Assert.Equal(sent.Id, (await bobMessages.NextAsync()).Id);

        await bobHub.InvokeAsync<bool>("MarkRead", room.Id, sent.Id);
        Assert.Equal(sent.Id, (await aliceReceipts.NextAsync()).LastReadMessageId);

        Assert.Equal(["alice", "bob"], await aliceHub.InvokeAsync<List<string>>("GetOnlineUsers"));
    }

    [SkippableFact]
    public async Task A_new_room_is_added_to_a_connection_on_the_other_instance()
    {
        Skip.If(_first is null || _second is null, $"Set {EnvironmentVariable} to run the backplane tests.");

        var alice = await _first!.SignInAsync("alice");
        var bob = await _second!.SignInAsync("bob");
        var aliceHub = await ConnectAsync(alice);
        var bobHub = await ConnectAsync(bob);
        var bobRooms = new HubEvents<RoomSummaryDto>(bobHub, nameof(IChatClient.RoomAdded));
        var bobMessages = new HubEvents<MessageDto>(bobHub, nameof(IChatClient.MessageReceived));

        // Created on the first instance; bob's connection lives on the second and must be
        // added to the room's group remotely.
        var room = await alice.PostAsync<RoomSummaryDto>("/api/direct", new { userName = "bob" });
        Assert.Equal(room.Id, (await bobRooms.NextAsync()).Id);

        await aliceHub.InvokeAsync<MessageDto>("SendMessage", room.Id, "new room");
        Assert.Equal("new room", (await bobMessages.NextAsync()).Body);
    }

    private async Task<HubConnection> ConnectAsync(TestUser user)
    {
        var connection = await user.ConnectAsync();
        _connections.Add(connection);
        return connection;
    }
}
