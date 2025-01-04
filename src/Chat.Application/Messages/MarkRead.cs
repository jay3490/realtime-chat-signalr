using Chat.Application.Abstractions;
using Chat.Application.Common;
using Chat.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Messages;

/// <summary>
/// Records that <paramref name="UserName"/> has seen every message in the room up to and
/// including <paramref name="MessageId"/>. Returns false when the marker was already there.
/// </summary>
public sealed record MarkReadCommand(Guid RoomId, string UserName, long MessageId) : ICommand<bool>;

internal sealed class MarkReadHandler(IChatDbContext db, TimeProvider clock)
    : ICommandHandler<MarkReadCommand, bool>
{
    public async Task<bool> HandleAsync(MarkReadCommand command, CancellationToken cancellationToken)
    {
        var room = await db.Rooms
            .Include(r => r.Members.Where(m => m.UserName == command.UserName))
            .SingleOrDefaultAsync(r => r.Id == command.RoomId, cancellationToken)
            ?? throw new NotFoundException("Room not found.");

        if (!room.HasMember(command.UserName))
        {
            throw new ForbiddenException("You are not a member of this room.");
        }

        var messageExists = await db.Messages
            .AnyAsync(m => m.Id == command.MessageId && m.RoomId == command.RoomId, cancellationToken);
        if (!messageExists)
        {
            throw new NotFoundException("Message not found in this room.");
        }

        var advanced = room.MarkRead(command.UserName, command.MessageId, clock.GetUtcNow());
        if (advanced)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return advanced;
    }
}
