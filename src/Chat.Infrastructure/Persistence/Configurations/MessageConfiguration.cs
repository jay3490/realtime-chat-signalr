using Chat.Domain.Messages;
using Chat.Domain.Rooms;
using Chat.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.Persistence.Configurations;

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedOnAdd();
        builder.Property(m => m.Sender).HasMaxLength(UserName.MaxLength).IsRequired();
        builder.Property(m => m.Body).HasMaxLength(Message.MaxBodyLength).IsRequired();
        builder.Ignore(m => m.DomainEvents);

        builder.HasOne<Room>()
            .WithMany()
            .HasForeignKey(m => m.RoomId)
            .OnDelete(DeleteBehavior.Cascade);

        // Serves keyset paging (WHERE RoomId = @room AND Id < @cursor ORDER BY Id DESC)
        // and unread counts (Id > LastReadMessageId).
        builder.HasIndex(m => new { m.RoomId, m.Id });
    }
}
