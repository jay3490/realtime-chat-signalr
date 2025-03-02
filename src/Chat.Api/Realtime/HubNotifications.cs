using Chat.Application.Abstractions;
using Chat.Application.Messages;
using Chat.Application.Realtime;
using Chat.Application.Rooms.Queries;
using Chat.Domain.Messages;
using Chat.Domain.Rooms;
using Microsoft.AspNetCore.SignalR;

namespace Chat.Api.Realtime;

/// <summary>
/// Pushes committed domain events to connected clients. Group and user sends go through
/// the hub lifetime manager, so with the Redis backplane enabled they reach connections on
/// every instance, not only this one.
/// </summary>
internal sealed class HubNotifications(
    IHubContext<ChatHub, IChatClient> hub,
    IPresenceTracker presence,
    IDispatcher dispatcher)
    : IDomainEventHandler<MessagePosted>,
      IDomainEventHandler<MessageEdited>,
      IDomainEventHandler<MessageDeleted>,
      IDomainEventHandler<MessagesRead>,
      IDomainEventHandler<MessagesDelivered>,
      IDomainEventHandler<RoomCreated>,
      IDomainEventHandler<MemberJoined>
{
    public Task HandleAsync(MessagePosted domainEvent, CancellationToken cancellationToken)
    {
        var message = MessageDto.From(domainEvent.Message);
        return hub.Clients.Group(ChatHub.RoomGroup(message.RoomId)).MessageReceived(message);
    }

    public Task HandleAsync(MessageEdited domainEvent, CancellationToken cancellationToken) =>
        hub.Clients.Group(ChatHub.RoomGroup(domainEvent.Message.RoomId)).MessageEdited(MessageDto.From(domainEvent.Message));

    public Task HandleAsync(MessageDeleted domainEvent, CancellationToken cancellationToken)
    {
        var message = domainEvent.Message;
        return hub.Clients.Group(ChatHub.RoomGroup(message.RoomId))
            .MessageDeleted(new MessageDeletedDto(message.RoomId, message.Id, message.DeletedAt!.Value));
    }

    public Task HandleAsync(MessagesRead domainEvent, CancellationToken cancellationToken) =>
        hub.Clients.Group(ChatHub.RoomGroup(domainEvent.RoomId)).ReadReceipt(new ReadReceiptDto(
            domainEvent.RoomId, domainEvent.UserName, domainEvent.LastReadMessageId, domainEvent.ReadAt));

    public Task HandleAsync(MessagesDelivered domainEvent, CancellationToken cancellationToken) =>
        hub.Clients.Group(ChatHub.RoomGroup(domainEvent.RoomId)).DeliveryReceipt(new DeliveryReceiptDto(
            domainEvent.RoomId, domainEvent.UserName, domainEvent.DeviceId, domainEvent.LastDeliveredMessageId, domainEvent.DeliveredAt));

    public async Task HandleAsync(RoomCreated domainEvent, CancellationToken cancellationToken)
    {
        foreach (var member in domainEvent.Members)
        {
            await AddUserToRoomGroupAsync(member, domainEvent.RoomId, cancellationToken);

            // A new room has no messages yet, so the summary can be built from the event
            // instead of reading it back.
            var name = domainEvent.Kind == RoomKind.Channel
                ? domainEvent.Name!
                : domainEvent.Members.First(m => m != member);
            await hub.Clients.User(member).RoomAdded(new RoomSummaryDto(domainEvent.RoomId, domainEvent.Kind, name, 0, 0, 0));
        }
    }

    public async Task HandleAsync(MemberJoined domainEvent, CancellationToken cancellationToken)
    {
        await AddUserToRoomGroupAsync(domainEvent.UserName, domainEvent.RoomId, cancellationToken);

        var summary = await dispatcher.QueryAsync(new GetCurrentRoomSummaryQuery(domainEvent.RoomId, domainEvent.UserName), cancellationToken);
        await hub.Clients.User(domainEvent.UserName).RoomAdded(summary);
        await hub.Clients.Group(ChatHub.RoomGroup(domainEvent.RoomId))
            .MemberJoined(new MemberJoinedDto(domainEvent.RoomId, domainEvent.UserName));
    }

    /// <summary>Adds every open connection of the user, on any instance, to the room's group.</summary>
    private async Task AddUserToRoomGroupAsync(string userName, Guid roomId, CancellationToken cancellationToken)
    {
        foreach (var connectionId in await presence.GetConnectionsAsync(userName, cancellationToken))
        {
            await hub.Groups.AddToGroupAsync(connectionId, ChatHub.RoomGroup(roomId), cancellationToken);
        }
    }
}
