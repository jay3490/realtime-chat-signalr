using Chat.Application.Realtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.Infrastructure.Presence;

public static class PresenceSetup
{
    public static IServiceCollection AddChatPresence(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PresenceOptions>(configuration.GetSection(PresenceOptions.SectionName));
        services.Configure<TypingOptions>(configuration.GetSection(TypingOptions.SectionName));
        services.AddSingleton<TypingThrottle>();
        services.AddSingleton<IPresenceTracker, InMemoryPresenceTracker>();
        return services;
    }
}
