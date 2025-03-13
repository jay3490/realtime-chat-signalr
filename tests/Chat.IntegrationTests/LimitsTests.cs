using Chat.Application.Messages;
using Chat.Application.Rooms.Queries;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chat.IntegrationTests;

/// <summary>Per-connection rate limiting and the hub's message size limit.</summary>
public sealed class LimitsTests : IAsyncLifetime
{
    private readonly ChatApiFactory _factory = new(settings: new Dictionary<string, string>
    {
        ["Realtime:Limits:MessagesPerSecond"] = "0.1",
        ["Realtime:Limits:MessageBurst"] = "3",
        ["Realtime:Limits:MaximumReceiveMessageSizeBytes"] = "4096",
    });

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
    public async Task A_connection_that_sends_too_fast_is_refused_without_affecting_others()
    {
        var (room, aliceHub, bobHub) = await TwoUsersAsync();

        for (var i = 0; i < 3; i++)
        {
            await aliceHub.InvokeAsync<MessageDto>("SendMessage", room, $"burst {i}");
        }

        var refused = await Assert.ThrowsAsync<HubException>(() => aliceHub.InvokeAsync<MessageDto>("SendMessage", room, "one too many"));
        Assert.Contains("too quickly", refused.Message, StringComparison.Ordinal);

        // Edits draw from the same allowance; reads and other connections are unaffected.
        await Assert.ThrowsAsync<HubException>(() => aliceHub.InvokeAsync<MessageDto>("EditMessage", room, 1L, "x"));
        Assert.NotEmpty(await aliceHub.InvokeAsync<List<string>>("GetOnlineUsers"));
        Assert.Equal("bob's turn", (await bobHub.InvokeAsync<MessageDto>("SendMessage", room, "bob's turn")).Body);
    }

    [Fact]
    public async Task A_hub_message_over_the_size_limit_closes_the_connection()
    {
        var (room, aliceHub, _) = await TwoUsersAsync();
        var closed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        aliceHub.Closed += error =>
        {
            closed.TrySetResult(error);
            return Task.CompletedTask;
        };

        await Assert.ThrowsAnyAsync<Exception>(() => aliceHub.InvokeAsync<MessageDto>("SendMessage", room, new string('x', 8000)));

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HubConnectionState.Disconnected, aliceHub.State);
    }

    private async Task<(Guid Room, HubConnection Alice, HubConnection Bob)> TwoUsersAsync()
    {
        var alice = await _factory.SignInAsync("alice");
        var bob = await _factory.SignInAsync("bob");
        var room = await alice.PostAsync<RoomSummaryDto>("/api/direct", new { userName = "bob" });
        var aliceHub = await alice.ConnectAsync();
        var bobHub = await bob.ConnectAsync();
        _connections.AddRange([aliceHub, bobHub]);
        return (room.Id, aliceHub, bobHub);
    }
}
