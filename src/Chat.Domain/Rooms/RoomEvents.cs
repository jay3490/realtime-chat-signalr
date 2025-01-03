using Chat.Domain.Common;

namespace Chat.Domain.Rooms;

public sealed record RoomCreated(
    Guid RoomId,
    RoomKind Kind,
    string? Name,
    IReadOnlyList<string> Members) : IDomainEvent;

public sealed record MemberJoined(Guid RoomId, string UserName) : IDomainEvent;

public sealed record MessagesRead(
    Guid RoomId,
    string UserName,
    long LastReadMessageId,
    DateTimeOffset ReadAt) : IDomainEvent;
