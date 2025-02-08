namespace Chat.Contracts;

/// <param name="Kind"><c>Channel</c> or <c>Direct</c>.</param>
/// <param name="Name">Channel name; null for a direct conversation.</param>
/// <param name="Members">Everyone who is a member from the start.</param>
public sealed record RoomCreated(
    Guid RoomId,
    string Kind,
    string? Name,
    IReadOnlyList<string> Members,
    string CreatedBy,
    DateTimeOffset CreatedAt) : IRoomEvent
{
    DateTimeOffset IChatEvent.OccurredAt => CreatedAt;
}

public sealed record MemberJoined(Guid RoomId, string UserName, DateTimeOffset JoinedAt) : IRoomEvent
{
    DateTimeOffset IChatEvent.OccurredAt => JoinedAt;
}

/// <param name="Mentions">Room members mentioned with <c>@name</c>, excluding the sender.</param>
public sealed record MessageSent(
    Guid RoomId,
    long MessageId,
    string Sender,
    string Body,
    IReadOnlyList<string> Mentions,
    DateTimeOffset SentAt) : IRoomEvent
{
    DateTimeOffset IChatEvent.OccurredAt => SentAt;
}

/// <param name="Revision">1 for the original text, incremented by every edit.</param>
public sealed record MessageEdited(
    Guid RoomId,
    long MessageId,
    string Sender,
    string Body,
    int Revision,
    DateTimeOffset EditedAt) : IRoomEvent
{
    DateTimeOffset IChatEvent.OccurredAt => EditedAt;
}

public sealed record MessageDeleted(Guid RoomId, long MessageId, string Sender, DateTimeOffset DeletedAt) : IRoomEvent
{
    DateTimeOffset IChatEvent.OccurredAt => DeletedAt;
}

/// <summary>A member's read marker moved forward to <paramref name="LastReadMessageId"/>.</summary>
public sealed record ReadReceiptRecorded(
    Guid RoomId,
    string UserName,
    long LastReadMessageId,
    DateTimeOffset ReadAt) : IRoomEvent
{
    DateTimeOffset IChatEvent.OccurredAt => ReadAt;
}

/// <summary>One of a member's devices has received every message up to <paramref name="LastDeliveredMessageId"/>.</summary>
public sealed record DeliveryRecorded(
    Guid RoomId,
    string UserName,
    string DeviceId,
    long LastDeliveredMessageId,
    DateTimeOffset DeliveredAt) : IRoomEvent
{
    DateTimeOffset IChatEvent.OccurredAt => DeliveredAt;
}

public sealed record UserRegistered(string UserName, DateTimeOffset RegisteredAt) : IUserEvent
{
    DateTimeOffset IChatEvent.OccurredAt => RegisteredAt;
}
