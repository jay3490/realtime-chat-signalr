using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.Persistence;

/// <summary>
/// The mapping shared by the write and read contexts, so both see the same schema.
/// </summary>
internal static class ChatModel
{
    public static void Configure(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ChatModel).Assembly);
}
