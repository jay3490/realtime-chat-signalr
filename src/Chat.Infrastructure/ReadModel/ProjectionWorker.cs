using Chat.Contracts;
using Chat.Infrastructure.EventLog;
using Chat.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chat.Infrastructure.ReadModel;

/// <summary>
/// Consumes every stream and applies each event to the read model. Partitions resume from
/// the checkpoints stored with the read model, not from the consumer group's offsets, so a
/// rebuilt (emptied) read model is replayed from the first offset. A failing event is
/// retried with back-off rather than skipped: skipping would leave the read model wrong.
/// </summary>
public sealed partial class ProjectionWorker(
    IServiceScopeFactory scopes,
    IEventLogConsumerFactory consumers,
    ChatMetrics metrics,
    TimeProvider clock,
    ILogger<ProjectionWorker> logger) : BackgroundService
{
    public const string GroupId = "chat-projections";

    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    // Consuming blocks the calling thread, so the loop gets one of its own.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Factory.StartNew(() => RunAsync(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        using var consumer = consumers.Create(new ConsumerSettings(GroupId, ResumeAt));
        consumer.Subscribe(EventStreams.All);
        LogStarted(logger);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var consumed = consumer.Consume(PollTimeout);
                if (consumed is null)
                {
                    continue;
                }

                await ProjectWithRetryAsync(consumed, stoppingToken);
                consumer.Commit(consumed);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; the current event is either committed or will be replayed.
        }
        finally
        {
            consumer.Close();
        }
    }

    private long? ResumeAt(string stream, int partition)
    {
        using var scope = scopes.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ReadModelProjector>().ResumeOffset(stream, partition);
    }

    private async Task ProjectWithRetryAsync(ConsumedEvent consumed, CancellationToken stoppingToken)
    {
        var backoff = TimeSpan.FromSeconds(1);
        while (true)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var projector = scope.ServiceProvider.GetRequiredService<ReadModelProjector>();
                var outcome = await projector.ProjectAsync(consumed, stoppingToken);
                var lag = consumed.Envelope is { } envelope ? clock.GetUtcNow() - envelope.OccurredAt : TimeSpan.Zero;
                metrics.Projected(outcome, consumed.Envelope?.EventType, lag);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogProjectionFailed(logger, ex, consumed.Stream, consumed.Partition, consumed.Offset, backoff.TotalSeconds);
                await Task.Delay(backoff, clock, stoppingToken);
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Read model projection started")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Projecting {Stream}/{Partition}@{Offset} failed; retrying in {DelaySeconds}s")]
    private static partial void LogProjectionFailed(
        ILogger logger, Exception exception, string stream, int partition, long offset, double delaySeconds);
}
