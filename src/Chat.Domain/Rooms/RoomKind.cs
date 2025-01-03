namespace Chat.Domain.Rooms;

public enum RoomKind
{
    /// <summary>A named room that anyone can discover and join.</summary>
    Channel = 0,

    /// <summary>A private conversation between exactly two users.</summary>
    Direct = 1,
}
