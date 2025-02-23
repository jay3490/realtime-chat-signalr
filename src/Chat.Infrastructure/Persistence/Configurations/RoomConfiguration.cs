using Chat.Domain.Rooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.Persistence.Configurations;

internal sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.ToTable("Rooms");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.Key).HasMaxLength(Room.MaxKeyLength).IsRequired();
        builder.Property(r => r.Name).HasMaxLength(Room.MaxNameLength);
        builder.Property(r => r.CreatedBy).HasMaxLength(Domain.Users.UserName.MaxLength).IsRequired();
        builder.HasIndex(r => r.Key).IsUnique();
        builder.Ignore(r => r.DomainEvents);

        builder.HasMany(r => r.Members)
            .WithOne()
            .HasForeignKey(m => m.RoomId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Members).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasData(SeedData.Channels.Select(c => SeedChannel(c.Id, c.Name)));
    }

    private static object SeedChannel(Guid id, string name) => new
    {
        Id = id,
        Kind = RoomKind.Channel,
        Key = Room.ChannelKey(name),
        Name = name,
        CreatedBy = SeedData.SystemUser,
        CreatedAt = SeedData.CreatedAt,
    };
}

internal static class SeedData
{
    public const string SystemUser = "system";
    public static readonly Guid GeneralChannelId = new("6f1c2a9e-0c6b-4d8e-9a51-6b1a3c2e7f01");
    public static readonly Guid RandomChannelId = new("6f1c2a9e-0c6b-4d8e-9a51-6b1a3c2e7f02");
    public static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Channels that exist from the start, in both the write model and the read model.</summary>
    public static readonly IReadOnlyList<(Guid Id, string Name)> Channels =
    [
        (GeneralChannelId, "general"),
        (RandomChannelId, "random"),
    ];
}
