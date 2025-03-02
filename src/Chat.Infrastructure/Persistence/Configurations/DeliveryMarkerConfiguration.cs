using Chat.Domain.Messages;
using Chat.Domain.Rooms;
using Chat.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.Persistence.Configurations;

internal sealed class DeliveryMarkerConfiguration : IEntityTypeConfiguration<DeliveryMarker>
{
    public void Configure(EntityTypeBuilder<DeliveryMarker> builder)
    {
        builder.ToTable("DeliveryMarkers");
        builder.HasKey(m => new { m.RoomId, m.UserName, m.DeviceId });
        builder.Property(m => m.UserName).HasMaxLength(UserName.MaxLength);
        builder.Property(m => m.DeviceId).HasMaxLength(DeliveryMarker.MaxDeviceIdLength);
        builder.Ignore(m => m.DomainEvents);

        builder.HasOne<Room>()
            .WithMany()
            .HasForeignKey(m => m.RoomId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
