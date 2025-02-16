using Chat.Domain.Common;

namespace Chat.Domain.Rooms;

public sealed record RoomCreated(
    Guid RoomId,
    RoomKind Kind,
    string? Name,
    IReadOnlyList<string> Members,
    string CreatedBy,
    DateTimeOffset CreatedAt) : IDomainEvent;

public sealed record MemberJoined(Guid RoomId, string UserName, DateTimeOffset JoinedAt) : IDomainEvent;

public sealed record MessagesRead(
    Guid RoomId,
    string UserName,
    long LastReadMessageId,
    DateTimeOffset ReadAt) : IDomainEvent;
