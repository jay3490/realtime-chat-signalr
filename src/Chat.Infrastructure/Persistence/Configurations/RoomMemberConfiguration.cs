using Chat.Domain.Rooms;
using Chat.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.Persistence.Configurations;

internal sealed class RoomMemberConfiguration : IEntityTypeConfiguration<RoomMember>
{
    public void Configure(EntityTypeBuilder<RoomMember> builder)
    {
        builder.ToTable("RoomMembers");
        builder.HasKey(m => new { m.RoomId, m.UserName });
        builder.Property(m => m.UserName).HasMaxLength(UserName.MaxLength);

        // "Which rooms am I in?" is asked on every connect and every room list load.
        builder.HasIndex(m => m.UserName);
    }
}
