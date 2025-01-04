using Chat.Application.Abstractions;
using Chat.Application.Common;
using Chat.Application.Persistence;
using Chat.Domain.Rooms;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Rooms;

/// <summary>Creates a public channel with the creator as its first member. Returns the room id.</summary>
public sealed record CreateChannelCommand(string Name, string CreatedBy) : ICommand<Guid>;

internal sealed class CreateChannelHandler(IChatDbContext db, TimeProvider clock)
    : ICommandHandler<CreateChannelCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateChannelCommand command, CancellationToken cancellationToken)
    {
        var room = Room.CreateChannel(command.Name, command.CreatedBy, clock.GetUtcNow());

        if (await db.Rooms.AnyAsync(r => r.Key == room.Key, cancellationToken))
        {
            throw new ConflictException($"A channel called '{room.Name}' already exists.");
        }

        db.Rooms.Add(room);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            // Someone created the same channel between the check and the insert.
            db.DiscardChanges();
            throw new ConflictException($"A channel called '{room.Name}' already exists.");
        }

        return room.Id;
    }
}
