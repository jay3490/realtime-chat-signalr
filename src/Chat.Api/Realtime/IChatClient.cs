using Chat.Application.Messages;
using Chat.Application.Rooms.Queries;

namespace Chat.Api.Realtime;

public sealed record ReadReceiptDto(Guid RoomId, string UserName, long LastReadMessageId, DateTimeOffset ReadAt);

public sealed record TypingDto(Guid RoomId, string UserName, int ExpiresInMs);

public sealed record TypingStoppedDto(Guid RoomId, string UserName);

public sealed record PresenceDto(string UserName, bool IsOnline);

public sealed record MemberJoinedDto(Guid RoomId, string UserName);

public sealed record ConnectionInfoDto(string UserName, string ConnectionId, string Instance);

/// <summary>Methods the server calls on connected clients. Names match the client's <c>connection.on(...)</c> handlers.</summary>
public interface IChatClient
{
    Task MessageReceived(MessageDto message);

    Task ReadReceipt(ReadReceiptDto receipt);

    Task UserTyping(TypingDto typing);

    Task UserStoppedTyping(TypingStoppedDto typing);

    Task PresenceChanged(PresenceDto presence);

    Task RoomAdded(RoomSummaryDto room);

    Task MemberJoined(MemberJoinedDto member);
}
