using System.Text.Json.Serialization;
using Chat.Api.Auth;
using Chat.Api.Errors;
using Chat.Infrastructure.Presence;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace Chat.Api.Realtime;

internal static class HubSetup
{
    public static IServiceCollection AddChatRealtime(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<LocalConnections>();
        services.AddSingleton<IUserIdProvider, UserNameIdProvider>();
        services.AddHostedService<PresenceHeartbeatService>();

        var signalR = services.AddSignalR(hub =>
            {
                hub.AddFilter<ChatHubExceptionFilter>();
            })
            .AddJsonProtocol(json => json.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // With a backplane, group and user sends are published to Redis and delivered by
        // whichever instance holds the target connection.
        var redisConnection = configuration.GetConnectionString("Redis");
        var redis = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();
        if (!string.IsNullOrWhiteSpace(redisConnection) && redis.Backplane)
        {
            signalR.AddStackExchangeRedis(redisConnection, options =>
                options.Configuration.ChannelPrefix = RedisChannel.Literal(redis.KeyPrefix));
        }

        return services;
    }

    /// <summary>Lets <c>Clients.User(name)</c> address every connection of a user by their user name.</summary>
    private sealed class UserNameIdProvider : IUserIdProvider
    {
        public string? GetUserId(HubConnectionContext connection) => connection.User.GetUserName();
    }

    /// <summary>
    /// Sends the reason for expected failures (not found, forbidden, validation) to the caller.
    /// Other exceptions keep SignalR's default of a generic error message.
    /// </summary>
    private sealed class ChatHubExceptionFilter : IHubFilter
    {
        public async ValueTask<object?> InvokeMethodAsync(
            HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
        {
            try
            {
                return await next(invocationContext);
            }
            catch (Exception ex) when (ChatExceptionHandler.StatusFor(ex) is not null)
            {
                throw new HubException(ex.Message);
            }
        }
    }
}
