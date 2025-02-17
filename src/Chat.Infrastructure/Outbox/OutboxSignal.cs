namespace Chat.Infrastructure.Outbox;

/// <summary>
/// Wakes the relay when this instance has just committed outbox rows, so events are
/// published immediately instead of on the next poll.
/// </summary>
public sealed class OutboxSignal : IDisposable
{
    private readonly SemaphoreSlim _pending = new(0, 1);

    public void Notify()
    {
        try
        {
            _pending.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled; one wake-up covers any number of saves.
        }
    }

    /// <summary>Waits for a notification or for <paramref name="timeout"/>, whichever comes first.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _pending.WaitAsync(timeout, cancellationToken);

    public void Dispose() => _pending.Dispose();
}
