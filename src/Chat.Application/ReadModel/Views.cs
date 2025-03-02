using Chat.Domain.Rooms;

namespace Chat.Application.ReadModel;

// Rows of the read model. They are built by the projection from published events and shaped
// for the queries that read them, so most values (member counts, unread counts, the latest
// message) are stored rather than computed on every read.

public sealed class RoomView
{
    public Guid Id { get; set; }

    public RoomKind Kind { get; set; }

    /// <summary>Channel name; null for direct conversations.</summary>
    public string? Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public int MemberCount { get; set; }

    /// <summary>Id of the newest message, deleted or not; 0 when the room is empty.</summary>
    public long LastMessageId { get; set; }

    public DateTimeOffset? LastActivityAt { get; set; }
}

public sealed class MemberView
{
    public Guid RoomId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public DateTimeOffset JoinedAt { get; set; }

    public long LastReadMessageId { get; set; }

    public DateTimeOffset? LastReadAt { get; set; }

    /// <summary>Messages from other members, not deleted, newer than the read marker.</summary>
    public int UnreadCount { get; set; }
}

public sealed class MessageView
{
    public long Id { get; set; }

    public Guid RoomId { get; set; }

    public string Sender { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public DateTimeOffset SentAt { get; set; }

    public int Revision { get; set; }

    public DateTimeOffset? EditedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>One version of a message's text: revision 1 as sent, then one row per edit.</summary>
public sealed class MessageRevisionView
{
    public long MessageId { get; set; }

    public int Revision { get; set; }

    public string Body { get; set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; set; }
}

public sealed class UserView
{
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset RegisteredAt { get; set; }
}

/// <summary>How far one device of a member has received the room's messages.</summary>
public sealed class DeliveryView
{
    public Guid RoomId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string DeviceId { get; set; } = string.Empty;

    public long LastDeliveredMessageId { get; set; }

    public DateTimeOffset DeliveredAt { get; set; }
}

/// <summary>
/// One entry of the change feed that devices sync from. <see cref="Position"/> is assigned
/// by the read database in the order the projection applies events, which is the order a
/// device must replay them in. Message entries point at the message rather than copying it,
/// so a device catching up gets each message's current state.
/// </summary>
public sealed class SyncEntry
{
    public long Position { get; set; }

    public Guid RoomId { get; set; }

    /// <summary>One of <see cref="SyncChangeKinds"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The message, or for a read receipt the message read up to.</summary>
    public long? MessageId { get; set; }

    /// <summary>Who joined, or who read.</summary>
    public string? UserName { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}

public static class SyncChangeKinds
{
    public const string RoomCreated = "RoomCreated";
    public const string MemberJoined = "MemberJoined";
    public const string MessageSent = "MessageSent";
    public const string MessageEdited = "MessageEdited";
    public const string MessageDeleted = "MessageDeleted";
    public const string ReadReceipt = "ReadReceipt";
}

/// <summary>
/// A single row describing the read model. <see cref="Epoch"/> changes whenever the read model
/// is rebuilt, because feed positions restart then; cursors from an older epoch are refused.
/// </summary>
public sealed class ReadModelState
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public Guid Epoch { get; set; }
}
