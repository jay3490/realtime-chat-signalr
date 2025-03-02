using Chat.Application.Abstractions;
using Chat.Application.Events;
using Chat.Application.Persistence;
using Chat.Contracts;
using Chat.Domain.Common;
using Chat.Domain.Messages;
using Chat.Domain.Rooms;
using Chat.Domain.Users;
using Chat.Infrastructure.Outbox;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Chat.Infrastructure.Persistence;

/// <summary>
/// Write model. Saving collects the domain events raised by tracked aggregates, stores
/// their published counterparts in the outbox in the same transaction as the change, and
/// dispatches the domain events in-process once that transaction has committed, so
/// listeners never observe a change that was rolled back.
/// </summary>
public sealed class ChatDbContext(
    DbContextOptions<ChatDbContext> options,
    IDomainEventDispatcher domainEvents,
    OutboxSignal? outboxSignal = null) : DbContext(options), IChatDbContext
{
    private const int SqliteConstraintUnique = 2067;
    private const int SqliteConstraintPrimaryKey = 1555;

    public DbSet<Room> Rooms => Set<Room>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<User> Users => Set<User>();

    public DbSet<DeliveryMarker> DeliveryMarkers => Set<DeliveryMarker>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var aggregates = ChangeTracker.Entries<IHasDomainEvents>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();
        var events = aggregates.SelectMany(a => a.DomainEvents).ToList();
        if (events.Count == 0)
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        var written = await SaveWithOutboxAsync(events, acceptAllChangesOnSuccess, cancellationToken);

        aggregates.ForEach(a => a.ClearDomainEvents());
        await domainEvents.DispatchAsync(events, cancellationToken);
        return written;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException("Use SaveChangesAsync so that domain events are dispatched.");

    public bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException switch
    {
        PostgresException pg => pg.SqlState == PostgresErrorCodes.UniqueViolation,
        SqliteException sqlite => sqlite.SqliteExtendedErrorCode is SqliteConstraintUnique or SqliteConstraintPrimaryKey,
        _ => false,
    };

    public void DiscardChanges() => ChangeTracker.Clear();

    /// <summary>
    /// Message ids come from the database, so the events that carry them can only be
    /// serialized after the first save. The outbox rows are written by a second save inside
    /// the same transaction: either the change and its events are stored, or neither is.
    /// </summary>
    private async Task<int> SaveWithOutboxAsync(
        List<IDomainEvent> events, bool acceptAllChangesOnSuccess, CancellationToken cancellationToken)
    {
        var transaction = Database.CurrentTransaction is null
            ? await Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var written = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

            var outbox = events
                .Select(IntegrationEvents.FromDomain)
                .OfType<IChatEvent>()
                .Select(e => OutboxMessage.From(EventEnvelope.Wrap(e, Guid.NewGuid())))
                .ToList();
            if (outbox.Count > 0)
            {
                OutboxMessages.AddRange(outbox);
                written += await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                if (outbox.Count > 0)
                {
                    outboxSignal?.Notify();
                }
            }

            return written;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) => ChatModel.Configure(modelBuilder);
}
