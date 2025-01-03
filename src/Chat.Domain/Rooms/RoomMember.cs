namespace Chat.Domain.Rooms;

/// <summary>
/// A user's membership in a room, including how far they have read.
/// </summary>
public sealed class RoomMember
{
    private RoomMember()
    {
    }

    internal RoomMember(Guid roomId, string userName, DateTimeOffset joinedAt)
    {
        RoomId = roomId;
        UserName = userName;
        JoinedAt = joinedAt;
    }

    public Guid RoomId { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public DateTimeOffset JoinedAt { get; private set; }

    /// <summary>Id of the newest message this member has seen; 0 when nothing has been read.</summary>
    public long LastReadMessageId { get; private set; }

    public DateTimeOffset? LastReadAt { get; private set; }

    /// <summary>
    /// Moves the read marker forward. Read markers never move backwards, so an older
    /// receipt arriving late (for example from a second device) is ignored.
    /// </summary>
    internal bool AdvanceReadMarker(long messageId, DateTimeOffset now)
    {
        if (messageId <= LastReadMessageId)
        {
            return false;
        }

        LastReadMessageId = messageId;
        LastReadAt = now;
        return true;
    }
}
