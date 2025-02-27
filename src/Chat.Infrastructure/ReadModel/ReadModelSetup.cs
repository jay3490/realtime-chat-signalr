using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>
    /// Runs the projection in this process. The Chat.Projections service does this in a
    /// deployment; the API does it only when the event log is in memory (single process).
    /// </summary>
    public static IServiceCollection AddReadModelProjection(this IServiceCollection services)
    {
        services.AddHostedService<ProjectionWorker>();
        return services;
    }
}
