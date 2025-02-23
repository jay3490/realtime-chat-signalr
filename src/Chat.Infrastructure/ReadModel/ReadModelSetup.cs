using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Chat.Infrastructure.ReadModel;

public static class ReadModelSetup
{
    /// <summary>Keeps the read model's migration history next to its tables, in the <c>read</c> schema.</summary>
    public static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql)
    {
        ArgumentNullException.ThrowIfNull(npgsql);
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ReadModelMapping.Schema);
    }
}
