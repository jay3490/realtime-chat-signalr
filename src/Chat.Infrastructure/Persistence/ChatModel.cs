using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.Persistence;

/// <summary>
/// The write model's mapping: the configurations in <c>Persistence.Configurations</c>. The
/// read model has its own, in <c>ReadModel.Configurations</c>.
/// </summary>
internal static class ChatModel
{
    public static void Configure(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ChatModel).Assembly,
            type => type.Namespace == typeof(Configurations.RoomConfiguration).Namespace);
}
