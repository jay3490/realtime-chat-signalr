using Chat.Application.Persistence;
using Chat.Application.ReadModel;
using Chat.Infrastructure.ReadModel;
using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.Persistence;

/// <summary>
/// Read-only context for the query side, over the read model the projection maintains. It
/// shares <see cref="ReadModelDbContext"/>'s mapping but tracks nothing and refuses to save:
/// only the projection writes the read model.
/// </summary>
public sealed class ChatReadDbContext(DbContextOptions<ChatReadDbContext> options) : DbContext(options), IChatReadDbContext
{
    public IQueryable<RoomView> Rooms => Set<RoomView>();

    public IQueryable<MemberView> Members => Set<MemberView>();

    public IQueryable<MessageView> Messages => Set<MessageView>();

    public IQueryable<MessageRevisionView> MessageRevisions => Set<MessageRevisionView>();

    public IQueryable<UserView> Users => Set<UserView>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new InvalidOperationException("The read context is read-only.");

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The read context is read-only.");

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

    protected override void OnModelCreating(ModelBuilder modelBuilder) => ReadModelMapping.Configure(modelBuilder, Database);
}
