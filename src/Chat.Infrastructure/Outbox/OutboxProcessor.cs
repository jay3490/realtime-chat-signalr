using Chat.Infrastructure.EventLog;
using Chat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Rows read and published per round.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// How long the relay sleeps when there is nothing to publish. Saves on this instance wake
    /// it straight away; this bounds the delay for rows written by other instances.
    /// </summary>
    public int PollIntervalMilliseconds { get; set; } = 1000;

    /// <summary>Pause after a failed round before trying again.</summary>
    public int RetryDelayMilliseconds { get; set; } = 2000;

    /// <summary>How long published rows are kept (for troubleshooting) before they are deleted.</summary>
    public int RetentionHours { get; set; } = 72;

    public TimeSpan PollInterval => TimeSpan.FromMilliseconds(PollIntervalMilliseconds);

    public TimeSpan RetryDelay => TimeSpan.FromMilliseconds(RetryDelayMilliseconds);

    public TimeSpan Retention => TimeSpan.FromHours(RetentionHours);
}

/// <param name="Fetched">Unpublished rows read in this round.</param>
/// <param name="Published">Rows acknowledged by the log and marked as published.</param>
public sealed record OutboxBatchResult(int Fetched, int Published)
{
    public bool Failed => Published < Fetched;
}

/// <summary>
/// Moves outbox rows to the event log, oldest first. A row is marked published only after
/// the log has acknowledged it and every row before it, so a crash at any point leads to
/// re-sending (at-least-once) rather than to a gap or a reordering.
/// </summary>
public sealed partial class OutboxProcessor(
    ChatDbContext db,
    IEventPublisher publisher,
    TimeProvider clock,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor> logger)
{
    private const int CleanupBatchSize = 1000;

    public async Task<OutboxBatchResult> PublishPendingAsync(CancellationToken cancellationToken)
    {
        var batch = await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.PublishedAt == null)
            .OrderBy(m => m.Id)
            .Take(options.Value.BatchSize)
            .ToListAsync(cancellationToken);
        if (batch.Count == 0)
        {
            return new OutboxBatchResult(0, 0);
        }

        var outgoing = batch
            .Select(m => new OutgoingEvent(m.EventId, m.EventType, m.Stream, m.StreamKey, m.Payload))
            .ToList();

        PublishResult result;
        try
        {
            result = await publisher.PublishAsync(outgoing, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new PublishResult(0, ex.Message);
        }

        var acknowledged = Math.Clamp(result.Acknowledged, 0, batch.Count);
        if (acknowledged > 0)
        {
            var publishedIds = batch.Take(acknowledged).Select(m => m.Id).ToList();
            var now = clock.GetUtcNow();
            await db.OutboxMessages
                .Where(m => publishedIds.Contains(m.Id))
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.PublishedAt, now), cancellationToken);
        }

        if (acknowledged < batch.Count)
        {
            var stuck = batch[acknowledged];
            var error = Truncate(result.Error ?? "The event log did not acknowledge the event.");
            await db.OutboxMessages
                .Where(m => m.Id == stuck.Id)
                .ExecuteUpdateAsync(
                    set => set.SetProperty(m => m.Attempts, m => m.Attempts + 1).SetProperty(m => m.LastError, error),
                    cancellationToken);
            LogStuck(logger, stuck.Id, stuck.EventType, stuck.Attempts + 1, error);
        }

        return new OutboxBatchResult(batch.Count, acknowledged);
    }

    /// <summary>Deletes rows published longer ago than the retention period.</summary>
    /// <remarks>
    /// Rows are published in id order, so the expired ones are the oldest published ids. They
    /// are found in id order and compared in memory, which keeps the query portable (SQLite
    /// cannot compare <see cref="DateTimeOffset"/> columns) and the deletes small.
    /// </remarks>
    public async Task<int> DeletePublishedAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow() - options.Value.Retention;
        var deleted = 0;
        while (true)
        {
            var oldest = await db.OutboxMessages
                .AsNoTracking()
                .Where(m => m.PublishedAt != null)
                .OrderBy(m => m.Id)
                .Select(m => new { m.Id, m.PublishedAt })
                .Take(CleanupBatchSize)
                .ToListAsync(cancellationToken);
            var expired = oldest.TakeWhile(m => m.PublishedAt < cutoff).Select(m => m.Id).ToList();
            if (expired.Count == 0)
            {
                return deleted;
            }

            deleted += await db.OutboxMessages.Where(m => expired.Contains(m.Id)).ExecuteDeleteAsync(cancellationToken);
            if (expired.Count < oldest.Count)
            {
                return deleted;
            }
        }
    }

    private static string Truncate(string error) =>
        error.Length <= OutboxMessage.MaxErrorLength ? error : error[..OutboxMessage.MaxErrorLength];

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Outbox row {Id} ({EventType}) was not published (attempt {Attempts}): {Error}")]
    private static partial void LogStuck(ILogger logger, long id, string eventType, int attempts, string error);
}
