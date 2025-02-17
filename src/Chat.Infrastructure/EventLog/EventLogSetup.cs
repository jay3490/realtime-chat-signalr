using Chat.Infrastructure.Outbox;
using Chat.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Chat.Infrastructure.EventLog;

public static class EventLogSetup
{
    /// <summary>
    /// Registers the event log: Kafka when <c>Kafka:BootstrapServers</c> is set, otherwise a
    /// log held in this process's memory.
    /// </summary>
    public static IServiceCollection AddChatEventLog(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(KafkaOptions.SectionName);
        services.Configure<KafkaOptions>(section);
        services.TryAddSingleton(TimeProvider.System);

        var kafka = section.Get<KafkaOptions>() ?? new KafkaOptions();
        if (kafka.IsConfigured)
        {
            services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
        }
        else
        {
            services.AddSingleton<InMemoryEventLog>();
            services.AddSingleton<IEventPublisher, InMemoryEventPublisher>();
        }

        return services;
    }

    /// <summary>
    /// Runs the outbox relay in this process. With PostgreSQL every replica runs it and an
    /// advisory lock picks the one that publishes; with SQLite there is only one process.
    /// </summary>
    public static IServiceCollection AddOutboxRelay(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.AddScoped<OutboxProcessor>();

        var database = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        if (database.Provider == DatabaseProvider.Postgres)
        {
            var connectionString = configuration.GetConnectionString("Chat")
                ?? throw new InvalidOperationException("ConnectionStrings:Chat is not configured.");
            services.AddSingleton<IOutboxLease>(sp =>
                new PostgresOutboxLease(connectionString, sp.GetRequiredService<ILogger<PostgresOutboxLease>>()));
        }
        else
        {
            services.AddSingleton<IOutboxLease, LocalOutboxLease>();
        }

        services.AddHostedService<OutboxRelayService>();
        return services;
    }
}
