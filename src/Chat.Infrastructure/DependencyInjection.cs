using Chat.Application.Persistence;
using Chat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddChatPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(DatabaseOptions.SectionName);
        services.Configure<DatabaseOptions>(section);
        var options = section.Get<DatabaseOptions>() ?? new DatabaseOptions();

        var writeConnection = configuration.GetConnectionString("Chat")
            ?? throw new InvalidOperationException("ConnectionStrings:Chat is not configured.");

        services.AddDbContext<ChatDbContext>(db => UseProvider(db, options.Provider, writeConnection));
        services.AddScoped<IChatDbContext>(sp => sp.GetRequiredService<ChatDbContext>());

        return services;
    }

    /// <summary>
    /// Brings the schema up to date when <see cref="DatabaseOptions.MigrateOnStartup"/> is set.
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
        var db = services.GetRequiredService<ChatDbContext>();
        if (options.Provider == DatabaseProvider.Sqlite)
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }
        else
        {
            await db.Database.MigrateAsync(cancellationToken);
        }
    }

    private static void UseProvider(DbContextOptionsBuilder db, DatabaseProvider provider, string connectionString)
    {
        switch (provider)
        {
            case DatabaseProvider.Postgres:
                db.UseNpgsql(connectionString);
                break;
            case DatabaseProvider.Sqlite:
                db.UseSqlite(connectionString);
                break;
            default:
                throw new InvalidOperationException($"Unsupported database provider '{provider}'.");
        }
    }
}
