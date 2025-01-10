using Chat.Application.Abstractions;
using Chat.Application.Persistence;
using Chat.Domain.Common;
using Chat.Domain.Messages;
using Chat.Domain.Rooms;
using Chat.Domain.Users;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Chat.Infrastructure.Persistence;

/// <summary>
/// Write model. Saving collects the domain events raised by tracked aggregates and
/// dispatches them once the transaction has committed, so listeners never observe a
/// change that was rolled back.
/// </summary>
public sealed class ChatDbContext(
    DbContextOptions<ChatDbContext> options,
    IDomainEventDispatcher domainEvents) : DbContext(options), IChatDbContext
{
    private const int SqliteConstraintUnique = 2067;

    public DbSet<Room> Rooms => Set<Room>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<User> Users => Set<User>();

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var aggregates = ChangeTracker.Entries<IHasDomainEvents>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();
        var events = aggregates.SelectMany(a => a.DomainEvents).ToList();

        var written = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        aggregates.ForEach(a => a.ClearDomainEvents());
        await domainEvents.DispatchAsync(events, cancellationToken);
        return written;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException("Use SaveChangesAsync so that domain events are dispatched.");

    public bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException switch
    {
        PostgresException pg => pg.SqlState == PostgresErrorCodes.UniqueViolation,
        SqliteException sqlite => sqlite.SqliteExtendedErrorCode == SqliteConstraintUnique,
        _ => false,
    };

    public void DiscardChanges() => ChangeTracker.Clear();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => ChatModel.Configure(modelBuilder);
}
