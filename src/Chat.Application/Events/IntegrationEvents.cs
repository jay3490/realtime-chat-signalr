using Chat.Contracts;
using Chat.Domain.Common;
using Chat.Domain.Messages;
using Chat.Domain.Users;
using DomainRooms = Chat.Domain.Rooms;

namespace Chat.Application.Events;

/// <summary>
/// Translates domain events, which are internal and may reference entities, into the
/// published contracts in <c>Chat.Contracts</c>. Domain events with no public counterpart map to null.
/// </summary>
public static class IntegrationEvents
{
    public static IChatEvent? FromDomain(IDomainEvent domainEvent) => domainEvent switch
    {
        DomainRooms.RoomCreated created => new RoomCreated(
            created.RoomId, created.Kind.ToString(), created.Name, created.Members, created.CreatedBy, created.CreatedAt),
        DomainRooms.MemberJoined joined => new MemberJoined(joined.RoomId, joined.UserName, joined.JoinedAt),
        DomainRooms.MessagesRead read => new ReadReceiptRecorded(read.RoomId, read.UserName, read.LastReadMessageId, read.ReadAt),
        MessagePosted posted => new MessageSent(
            posted.Message.RoomId,
            posted.Message.Id,
            posted.Message.Sender,
            posted.Message.Body,
            posted.Mentions,
            posted.Message.SentAt),
        Domain.Messages.MessageEdited edited => new Contracts.MessageEdited(
            edited.Message.RoomId,
            edited.Message.Id,
            edited.Message.Sender,
            edited.Message.Body,
            edited.Message.Revision,
            edited.Message.EditedAt ?? throw new InvalidOperationException("An edited message has no edit time.")),
        Domain.Messages.MessageDeleted deleted => new Contracts.MessageDeleted(
            deleted.Message.RoomId,
            deleted.Message.Id,
            deleted.Message.Sender,
            deleted.Message.DeletedAt ?? throw new InvalidOperationException("A deleted message has no deletion time.")),
        Domain.Users.UserRegistered registered => new Contracts.UserRegistered(registered.UserName, registered.RegisteredAt),
        _ => null,
    };
}
