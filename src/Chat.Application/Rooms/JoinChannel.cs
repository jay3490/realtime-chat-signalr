using Chat.Application.Abstractions;
using Chat.Application.Common;
using Chat.Application.Persistence;
using Chat.Domain.Rooms;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Rooms;

/// <summary>Adds the user to a channel. Joining a channel you are already in is a no-op.</summary>
public sealed record JoinChannelCommand(Guid RoomId, string UserName) : ICommand<Guid>;

internal sealed class JoinChannelHandler(IChatDbContext db, TimeProvider clock)
    : ICommandHandler<JoinChannelCommand, Guid>
{
    public async Task<Guid> HandleAsync(JoinChannelCommand command, CancellationToken cancellationToken)
    {
        var room = await db.Rooms
            .Include(r => r.Members.Where(m => m.UserName == command.UserName))
            .SingleOrDefaultAsync(r => r.Id == command.RoomId, cancellationToken);

        // Direct conversations are reported as missing rather than forbidden so that
        // their ids cannot be probed.
        if (room is null || room.Kind != RoomKind.Channel)
        {
            throw new NotFoundException("Channel not found.");
        }

        room.Join(command.UserName, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return room.Id;
    }
}
