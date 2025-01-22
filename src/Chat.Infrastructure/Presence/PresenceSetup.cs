using Chat.Application.Realtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Chat.Infrastructure.Presence;

public static class PresenceSetup
{
    /// <summary>
    /// Registers the presence tracker: Redis-backed when <c>ConnectionStrings:Redis</c> is
    /// set, in-memory (single instance only) otherwise.
    /// </summary>
    public static IServiceCollection AddChatPresence(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PresenceOptions>(configuration.GetSection(PresenceOptions.SectionName));
        services.Configure<TypingOptions>(configuration.GetSection(TypingOptions.SectionName));
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.AddSingleton<TypingThrottle>();

        var redisConnection = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddSingleton<IPresenceTracker, InMemoryPresenceTracker>();
            return services;
        }

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));
        services.AddSingleton<IPresenceTracker, RedisPresenceTracker>();
        return services;
    }
}
