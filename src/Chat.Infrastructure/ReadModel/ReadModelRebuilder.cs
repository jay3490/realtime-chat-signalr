using Chat.Application.ReadModel;
using Chat.Domain.Rooms;
using Chat.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.ReadModel;

/// <summary>
/// Empties the read model, checkpoints included, so the projection replays every stream
/// from its first offset. The seeded channels are put back because no event creates them.
/// </summary>
public sealed class ReadModelRebuilder(ReadModelDbContext db)
{
    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.Checkpoints.ExecuteDeleteAsync(cancellationToken);
        await db.ProcessedEvents.ExecuteDeleteAsync(cancellationToken);
        await db.MessageRevisions.ExecuteDeleteAsync(cancellationToken);
        await db.Messages.ExecuteDeleteAsync(cancellationToken);
        await db.Members.ExecuteDeleteAsync(cancellationToken);
        await db.Users.ExecuteDeleteAsync(cancellationToken);
        await db.Rooms.ExecuteDeleteAsync(cancellationToken);

        db.ChangeTracker.Clear();
        db.Rooms.AddRange(SeedData.Channels.Select(c => new RoomView
        {
            Id = c.Id,
            Kind = RoomKind.Channel,
            Name = c.Name,
            CreatedAt = SeedData.CreatedAt,
        }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
