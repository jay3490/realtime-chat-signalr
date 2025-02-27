using Chat.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Persistence;

/// <summary>
/// Membership checks. They read the write model, not the read model: an access decision must
/// not lag behind a join, and a membership lookup by primary key is cheap.
/// </summary>
internal static class RoomAccess
{
    public static Task<bool> IsMemberAsync(
        this IChatDbContext db, Guid roomId, string userName, CancellationToken cancellationToken) =>
        db.Rooms
            .Where(r => r.Id == roomId)
            .SelectMany(r => r.Members)
            .AnyAsync(m => m.UserName == userName, cancellationToken);

    /// <summary>Throws unless <paramref name="userName"/> is a member of the room.</summary>
    public static async Task EnsureMemberAsync(
        this IChatDbContext db, Guid roomId, string userName, CancellationToken cancellationToken)
    {
        if (await db.IsMemberAsync(roomId, userName, cancellationToken))
        {
            return;
        }

        if (await db.Rooms.AnyAsync(r => r.Id == roomId, cancellationToken))
        {
            throw new ForbiddenException("You are not a member of this room.");
        }

        throw new NotFoundException("Room not found.");
    }
}
