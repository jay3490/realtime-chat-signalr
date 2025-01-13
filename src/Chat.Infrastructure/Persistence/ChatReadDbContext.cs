using Chat.Application.Persistence;
using Chat.Domain.Messages;
using Chat.Domain.Rooms;
using Chat.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.Persistence;

/// <summary>
/// Read-only context for the query side. It shares the write model's mapping but tracks
/// nothing, refuses to save and can be pointed at a separate connection (for example a
/// streaming replica) through <c>ConnectionStrings:ChatRead</c>.
/// </summary>
public sealed class ChatReadDbContext(DbContextOptions<ChatReadDbContext> options) : DbContext(options), IChatReadDbContext
{
    public IQueryable<Room> Rooms => Set<Room>();

    public IQueryable<RoomMember> RoomMembers => Set<RoomMember>();

    public IQueryable<Message> Messages => Set<Message>();

    public IQueryable<User> Users => Set<User>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new InvalidOperationException("The read context is read-only.");

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The read context is read-only.");

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

    protected override void OnModelCreating(ModelBuilder modelBuilder) => ChatModel.Configure(modelBuilder);
}
