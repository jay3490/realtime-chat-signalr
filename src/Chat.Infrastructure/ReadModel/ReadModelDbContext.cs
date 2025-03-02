using Chat.Application.ReadModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Chat.Infrastructure.ReadModel;

/// <summary>
/// The read model as the projection sees it: the only writer. It lives in its own database
/// (SQLite) or its own <c>read</c> schema (PostgreSQL), so it can be dropped and rebuilt from
/// the event log without touching the write model.
/// </summary>
public sealed class ReadModelDbContext(DbContextOptions<ReadModelDbContext> options) : DbContext(options)
{
    public DbSet<RoomView> Rooms => Set<RoomView>();

    public DbSet<MemberView> Members => Set<MemberView>();

    public DbSet<MessageView> Messages => Set<MessageView>();

    public DbSet<MessageRevisionView> MessageRevisions => Set<MessageRevisionView>();

    public DbSet<UserView> Users => Set<UserView>();

    public DbSet<DeliveryView> Deliveries => Set<DeliveryView>();

    public DbSet<SyncEntry> SyncEntries => Set<SyncEntry>();

    public DbSet<ReadModelState> State => Set<ReadModelState>();

    public DbSet<ProjectionCheckpoint> Checkpoints => Set<ProjectionCheckpoint>();

    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => ReadModelMapping.Configure(modelBuilder, Database);
}

/// <summary>The mapping shared by the projection's context and the query-side context.</summary>
internal static class ReadModelMapping
{
    public const string Schema = "read";

    public static void Configure(ModelBuilder modelBuilder, DatabaseFacade database)
    {
        if (database.IsNpgsql())
        {
            modelBuilder.HasDefaultSchema(Schema);
        }

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ReadModelMapping).Assembly,
            type => type.Namespace == typeof(Configurations.RoomViewConfiguration).Namespace);
    }
}
