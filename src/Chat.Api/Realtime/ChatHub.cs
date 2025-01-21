using Chat.Api.Auth;
using Chat.Application.Abstractions;
using Chat.Application.Messages;
using Chat.Application.Realtime;
using Chat.Application.Rooms.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Chat.Api.Realtime;

/// <summary>
/// The real-time endpoint. Each room is a SignalR group; a connection joins the groups of
/// all its user's rooms when it connects. Messages and read receipts are not broadcast
/// from here: the hub runs the command, and the domain event it raises is what reaches
/// the room (see <see cref="HubNotifications"/>), whichever entry point was used.
/// </summary>
[Authorize]
public sealed class ChatHub(
    IDispatcher dispatcher,
    IPresenceTracker presence,
    TypingThrottle typing,
    LocalConnections localConnections) : Hub<IChatClient>
{
    public const string Path = "/hubs/chat";

    public static string RoomGroup(Guid roomId) => $"room:{roomId:N}";

    private string UserName => Context.User!.GetUserName();

    public override async Task OnConnectedAsync()
    {
        var userName = UserName;
        localConnections.Add(Context.ConnectionId, userName);

        // Register first: a room created while the groups below are being joined will then
        // find this connection through the presence tracker.
        var cameOnline = await presence.ConnectAsync(userName, Context.ConnectionId, Context.ConnectionAborted);

        var roomIds = await dispatcher.QueryAsync(new GetMyRoomIdsQuery(userName), Context.ConnectionAborted);
        foreach (var roomId in roomIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(roomId), Context.ConnectionAborted);
        }

        if (cameOnline)
        {
            await Clients.Others.PresenceChanged(new PresenceDto(userName, IsOnline: true));
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userName = UserName;
        localConnections.Remove(Context.ConnectionId);

        if (await presence.DisconnectAsync(userName, Context.ConnectionId))
        {
            await Clients.Others.PresenceChanged(new PresenceDto(userName, IsOnline: false));
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task<MessageDto> SendMessage(Guid roomId, string body)
    {
        var message = await dispatcher.SendAsync(new SendMessageCommand(roomId, UserName, body), Context.ConnectionAborted);

        // The message itself ends the typing indicator on clients; let the next keystroke
        // start a new one straight away.
        typing.Reset(roomId, UserName);
        return message;
    }

    public Task<bool> MarkRead(Guid roomId, long messageId) =>
        dispatcher.SendAsync(new MarkReadCommand(roomId, UserName, messageId), Context.ConnectionAborted);

    /// <summary>Called by clients as the user types. Throttled, so it is safe to call on every keystroke.</summary>
    public async Task StartTyping(Guid roomId)
    {
        var userName = UserName;
        if (!typing.TryBegin(roomId, userName))
        {
            return;
        }

        if (!await dispatcher.QueryAsync(new IsRoomMemberQuery(roomId, userName), Context.ConnectionAborted))
        {
            typing.Reset(roomId, userName);
            throw new HubException("You are not a member of this room.");
        }

        var expiresInMs = (int)typing.IndicatorDuration.TotalMilliseconds;
        await Clients.OthersInGroup(RoomGroup(roomId)).UserTyping(new TypingDto(roomId, userName, expiresInMs));
    }

    /// <summary>Called when the user clears the input without sending.</summary>
    public async Task StopTyping(Guid roomId)
    {
        var userName = UserName;
        typing.Reset(roomId, userName);
        if (await dispatcher.QueryAsync(new IsRoomMemberQuery(roomId, userName), Context.ConnectionAborted))
        {
            await Clients.OthersInGroup(RoomGroup(roomId)).UserStoppedTyping(new TypingStoppedDto(roomId, userName));
        }
    }

    public Task<IReadOnlyList<string>> GetOnlineUsers() => presence.GetOnlineUsersAsync(Context.ConnectionAborted);

    /// <summary>Which instance is serving this connection; handy when running several behind a load balancer.</summary>
    public ConnectionInfoDto GetConnectionInfo() =>
        new(UserName, Context.ConnectionId, Environment.MachineName);
}
