using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.Outbox;

/// <summary>
/// Publishes the outbox continuously on the instance holding the lease. It drains full
/// batches back to back, sleeps until signalled or polled when idle, and backs off after
/// a failure. Published rows past their retention are deleted about once an hour.
/// </summary>
internal sealed partial class OutboxRelayService(
    IServiceScopeFactory scopes,
    IOutboxLease lease,
    OutboxSignal signal,
    TimeProvider clock,
    IOptions<OutboxOptions> options,
    ILogger<OutboxRelayService> logger) : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var nextCleanup = clock.GetUtcNow();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await lease.TryAcquireAsync(stoppingToken))
                {
                    await Task.Delay(settings.PollInterval * 5, clock, stoppingToken);
                    continue;
                }

                await using var scope = scopes.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
                var result = await processor.PublishPendingAsync(stoppingToken);

                if (clock.GetUtcNow() >= nextCleanup)
                {
                    await processor.DeletePublishedAsync(stoppingToken);
                    nextCleanup = clock.GetUtcNow() + CleanupInterval;
                }

                if (result.Failed)
                {
                    await Task.Delay(settings.RetryDelay, clock, stoppingToken);
                }
                else if (result.Fetched < settings.BatchSize)
                {
                    await signal.WaitAsync(settings.PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogRoundFailed(logger, ex);
                try
                {
                    await Task.Delay(settings.RetryDelay, clock, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox relay round failed; retrying")]
    private static partial void LogRoundFailed(ILogger logger, Exception exception);
}
