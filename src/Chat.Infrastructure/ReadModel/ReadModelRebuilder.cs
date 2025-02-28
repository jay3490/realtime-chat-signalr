using Chat.Application.ReadModel;
using Chat.Domain.Rooms;
using Chat.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Chat.Infrastructure.ReadModel;

/// <summary>
/// Empties the read model, checkpoints included, so the projection replays every stream
/// from its first offset. The seeded channels are put back because no event creates them.
/// </summary>
public sealed partial class ReadModelRebuilder(ReadModelDbContext db, ILogger<ReadModelRebuilder> logger)
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
        LogReset(logger);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Read model emptied; every stream will be replayed from its first offset")]
    private static partial void LogReset(ILogger logger);
}

public static class ReadModelRebuild
{
    /// <summary>Empties the read model; the projection then replays the log from the start.</summary>
    public static async Task ResetReadModelAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReadModelRebuilder>().ResetAsync(cancellationToken);
    }
}
