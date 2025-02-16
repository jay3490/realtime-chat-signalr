using Chat.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedOnAdd();
        builder.Property(m => m.Stream).HasMaxLength(32).IsRequired();
        builder.Property(m => m.StreamKey).HasMaxLength(64).IsRequired();
        builder.Property(m => m.EventType).HasMaxLength(64).IsRequired();
        builder.Property(m => m.Payload).IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(OutboxMessage.MaxErrorLength);
        builder.HasIndex(m => m.EventId).IsUnique();

        // The relay only ever asks for the oldest unpublished rows; published rows wait
        // for the cleanup and should not bloat that lookup.
        builder.HasIndex(m => m.Id)
            .HasDatabaseName("IX_OutboxMessages_Unpublished")
            .HasFilter("\"PublishedAt\" IS NULL");
    }
}
