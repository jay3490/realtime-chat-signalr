using Chat.Application.Abstractions;
using Chat.Application.Common;
using Chat.Application.Persistence;
using Chat.Domain.Rooms;
using Chat.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Rooms;

/// <summary>
/// Returns the direct conversation between two users, creating it the first time either
/// of them opens it.
/// </summary>
public sealed record OpenDirectConversationCommand(string UserName, string OtherUserName) : ICommand<Guid>;

internal sealed class OpenDirectConversationHandler(IChatDbContext db, TimeProvider clock)
    : ICommandHandler<OpenDirectConversationCommand, Guid>
{
    public async Task<Guid> HandleAsync(OpenDirectConversationCommand command, CancellationToken cancellationToken)
    {
        var other = UserName.Normalize(command.OtherUserName);
        if (!await db.Users.AnyAsync(u => u.Name == other, cancellationToken))
        {
            throw new NotFoundException($"No user called '{other}' has signed in yet.");
        }

        var key = Room.DirectKey(command.UserName, other);
        var existing = await FindAsync(key, cancellationToken);
        if (existing is not null)
        {
            return existing.Value;
        }

        var room = Room.CreateDirect(command.UserName, other, clock.GetUtcNow());
        db.Rooms.Add(room);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return room.Id;
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            // The other user opened the conversation at the same moment; use theirs.
            db.DiscardChanges();
            return await FindAsync(key, cancellationToken)
                ?? throw new InvalidOperationException("Direct conversation vanished after a key conflict.");
        }
    }

    private async Task<Guid?> FindAsync(string key, CancellationToken cancellationToken) =>
        await db.Rooms
            .Where(r => r.Key == key)
            .Select(r => (Guid?)r.Id)
            .SingleOrDefaultAsync(cancellationToken);
}
