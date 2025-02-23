using Chat.Application.ReadModel;
using Chat.Domain.Messages;
using Chat.Domain.Rooms;
using Chat.Domain.Users;
using Chat.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.ReadModel.Configurations;

// The read model has no foreign keys: every row is derived from the event log, and the
// projection must be able to apply events even if a related row is missing.

internal sealed class RoomViewConfiguration : IEntityTypeConfiguration<RoomView>
{
    public void Configure(EntityTypeBuilder<RoomView> builder)
    {
        builder.ToTable("Rooms");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.Name).HasMaxLength(Room.MaxNameLength);
        builder.HasIndex(r => new { r.Kind, r.Name });
        builder.HasData(SeedData.Channels.Select(c => new RoomView
        {
            Id = c.Id,
            Kind = RoomKind.Channel,
            Name = c.Name,
            CreatedAt = SeedData.CreatedAt,
        }));
    }
}

internal sealed class MemberViewConfiguration : IEntityTypeConfiguration<MemberView>
{
    public void Configure(EntityTypeBuilder<MemberView> builder)
    {
        builder.ToTable("Members");
        builder.HasKey(m => new { m.RoomId, m.UserName });
        builder.Property(m => m.UserName).HasMaxLength(UserName.MaxLength);
        builder.HasIndex(m => m.UserName);
    }
}

internal sealed class MessageViewConfiguration : IEntityTypeConfiguration<MessageView>
{
    public void Configure(EntityTypeBuilder<MessageView> builder)
    {
        builder.ToTable("Messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Sender).HasMaxLength(UserName.MaxLength).IsRequired();
        builder.Property(m => m.Body).HasMaxLength(Message.MaxBodyLength).IsRequired();
        builder.HasIndex(m => new { m.RoomId, m.Id });
    }
}

internal sealed class MessageRevisionViewConfiguration : IEntityTypeConfiguration<MessageRevisionView>
{
    public void Configure(EntityTypeBuilder<MessageRevisionView> builder)
    {
        builder.ToTable("MessageRevisions");
        builder.HasKey(r => new { r.MessageId, r.Revision });
        builder.Property(r => r.Body).HasMaxLength(Message.MaxBodyLength).IsRequired();
    }
}

internal sealed class UserViewConfiguration : IEntityTypeConfiguration<UserView>
{
    public void Configure(EntityTypeBuilder<UserView> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Name);
        builder.Property(u => u.Name).HasMaxLength(UserName.MaxLength);
    }
}

internal sealed class ProjectionCheckpointConfiguration : IEntityTypeConfiguration<ProjectionCheckpoint>
{
    public void Configure(EntityTypeBuilder<ProjectionCheckpoint> builder)
    {
        builder.ToTable("ProjectionCheckpoints");
        builder.HasKey(c => new { c.Stream, c.Partition });
        builder.Property(c => c.Stream).HasMaxLength(32);
    }
}

internal sealed class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        builder.ToTable("ProcessedEvents");
        builder.HasKey(e => e.EventId);
        builder.Property(e => e.EventType).HasMaxLength(64);
    }
}
