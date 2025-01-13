using Chat.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Persistence;

internal static class RoomAccess
{
    /// <summary>Throws unless <paramref name="userName"/> is a member of the room.</summary>
    public static async Task EnsureMemberAsync(
        this IChatReadDbContext read, Guid roomId, string userName, CancellationToken cancellationToken)
    {
        if (await read.RoomMembers.AnyAsync(m => m.RoomId == roomId && m.UserName == userName, cancellationToken))
        {
            return;
        }

        if (await read.Rooms.AnyAsync(r => r.Id == roomId, cancellationToken))
        {
            throw new ForbiddenException("You are not a member of this room.");
        }

        throw new NotFoundException("Room not found.");
    }
}
