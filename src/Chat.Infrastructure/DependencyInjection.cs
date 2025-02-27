using Chat.Application.Persistence;
using Chat.Infrastructure.Outbox;
using Chat.Infrastructure.Persistence;
using Chat.Infrastructure.ReadModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Chat.Infrastructure;

public static class DependencyInjection
{
    /// <summary>The write model (<c>ConnectionStrings:Chat</c>) and its outbox.</summary>
    public static IServiceCollection AddChatPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var options = DatabaseOptionsFrom(services, configuration);
        var writeConnection = configuration.GetConnectionString("Chat")
            ?? throw new InvalidOperationException("ConnectionStrings:Chat is not configured.");

        services.AddSingleton<OutboxSignal>();
        services.AddDbContext<ChatDbContext>(db => UseProvider(db, options.Provider, writeConnection));
        services.AddScoped<IChatDbContext>(sp => sp.GetRequiredService<ChatDbContext>());
        return services;
    }

    /// <summary>
    /// The read model (<c>ConnectionStrings:ChatRead</c>): the query-side context used by query
    /// handlers, and the projection's writable context. With PostgreSQL the read model may
    /// share the write database, since it lives in its own schema; SQLite needs its own file.
    /// </summary>
    public static IServiceCollection AddChatReadModel(this IServiceCollection services, IConfiguration configuration)
    {
        var options = DatabaseOptionsFrom(services, configuration);
        var readConnection = configuration.GetConnectionString("ChatRead");
        if (string.IsNullOrWhiteSpace(readConnection))
        {
            readConnection = options.Provider == DatabaseProvider.Postgres
                ? configuration.GetConnectionString("Chat")
                : null;
        }

        if (string.IsNullOrWhiteSpace(readConnection))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:ChatRead is not configured. With SQLite the read model needs its own database file.");
        }

        services.AddDbContext<ReadModelDbContext>(db => UseProvider(db, options.Provider, readConnection, ReadModelSetup.ConfigureNpgsql));
        services.AddDbContext<ChatReadDbContext>(db => UseProvider(db, options.Provider, readConnection, ReadModelSetup.ConfigureNpgsql));
        services.AddScoped<IChatReadDbContext>(sp => sp.GetRequiredService<ChatReadDbContext>());
        services.AddScoped<ReadModelProjector>();
        services.AddScoped<ReadModelRebuilder>();
        return services;
    }

    /// <summary>
    /// Brings the schemas up to date when <see cref="DatabaseOptions.MigrateOnStartup"/> is set.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        if (!options.MigrateOnStartup)
        {
            return;
        }

        await MigrateAsync(scope.ServiceProvider, options, cancellationToken);
    }

    /// <summary>Applies migrations unconditionally; used by the one-shot migrator container.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        await MigrateAsync(scope.ServiceProvider, options, cancellationToken);
    }

    private static async Task MigrateAsync(IServiceProvider services, DatabaseOptions options, CancellationToken cancellationToken)
    {
        var databases = new List<DbContext>();
        if (services.GetService<ChatDbContext>() is { } writeModel)
        {
            databases.Add(writeModel);
        }

        if (services.GetService<ReadModelDbContext>() is { } readModel)
        {
            databases.Add(readModel);
        }

        foreach (var db in databases)
        {
            if (options.Provider == DatabaseProvider.Sqlite)
            {
                await db.Database.EnsureCreatedAsync(cancellationToken);
            }
            else
            {
                await db.Database.MigrateAsync(cancellationToken);
            }
        }
    }

    private static DatabaseOptions DatabaseOptionsFrom(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(DatabaseOptions.SectionName);
        services.Configure<DatabaseOptions>(section);
        return section.Get<DatabaseOptions>() ?? new DatabaseOptions();
    }

    private static void UseProvider(
        DbContextOptionsBuilder db,
        DatabaseProvider provider,
        string connectionString,
        Action<NpgsqlDbContextOptionsBuilder>? npgsql = null)
    {
        switch (provider)
        {
            case DatabaseProvider.Postgres:
                db.UseNpgsql(connectionString, npgsql);
                break;
            case DatabaseProvider.Sqlite:
                db.UseSqlite(connectionString);
                break;
            default:
                throw new InvalidOperationException($"Unsupported database provider '{provider}'.");
        }
    }
}
