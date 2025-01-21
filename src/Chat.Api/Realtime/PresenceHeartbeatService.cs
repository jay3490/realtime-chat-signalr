using Chat.Application.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Chat.Api.Realtime;

/// <summary>
/// Keeps this instance's presence leases alive and expires leases that nobody renewed,
/// which is how users connected to a crashed instance eventually show as offline.
/// </summary>
internal sealed partial class PresenceHeartbeatService(
    IPresenceTracker presence,
    LocalConnections localConnections,
    IHubContext<ChatHub, IChatClient> hub,
    IOptions<PresenceOptions> options,
    TimeProvider clock,
    ILogger<PresenceHeartbeatService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.RenewInterval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await BeatAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogBeatFailed(logger, ex);
            }
        }
    }

    internal async Task BeatAsync(CancellationToken cancellationToken)
    {
        await presence.RenewAsync(localConnections.Snapshot(), cancellationToken);

        foreach (var userName in await presence.RemoveExpiredAsync(cancellationToken))
        {
            await hub.Clients.All.PresenceChanged(new PresenceDto(userName, IsOnline: false));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Presence heartbeat failed; will retry on the next tick")]
    private static partial void LogBeatFailed(ILogger logger, Exception exception);
}
