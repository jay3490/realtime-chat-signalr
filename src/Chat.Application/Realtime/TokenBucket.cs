namespace Chat.Application.Realtime;

public sealed class RealtimeLimitsOptions
{
    public const string SectionName = "Realtime:Limits";

    /// <summary>Sustained rate of message commands (send, edit, delete) allowed per connection.</summary>
    public double MessagesPerSecond { get; set; } = 5;

    /// <summary>How many message commands a connection may make back to back before the rate applies.</summary>
    public int MessageBurst { get; set; } = 10;

    /// <summary>
    /// Largest single hub message the server accepts; a bigger one closes the connection. A
    /// 4000-character message body is at most about 12 KB of UTF-8, so 16 KB leaves room for
    /// the envelope without letting clients push large payloads.
    /// </summary>
    public long MaximumReceiveMessageSizeBytes { get; set; } = 16 * 1024;

    /// <summary>Largest HTTP request body accepted by the API.</summary>
    public long MaxRequestBodySizeBytes { get; set; } = 64 * 1024;
}

/// <summary>
/// A token bucket: holds up to <c>burst</c> permits and refills continuously at
/// <c>permitsPerSecond</c>. Each connection gets its own, so one noisy client cannot use up
/// anyone else's allowance.
/// </summary>
public sealed class TokenBucket
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly double _permitsPerSecond;
    private readonly int _burst;
    private double _tokens;
    private long _lastRefill;

    public TokenBucket(TimeProvider clock, double permitsPerSecond, int burst)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(permitsPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(burst);

        _clock = clock;
        _permitsPerSecond = permitsPerSecond;
        _burst = burst;
        _tokens = burst;
        _lastRefill = clock.GetTimestamp();
    }

    /// <summary>Takes one permit if one is available.</summary>
    public bool TryTake()
    {
        lock (_gate)
        {
            var now = _clock.GetTimestamp();
            var elapsed = _clock.GetElapsedTime(_lastRefill, now);
            _lastRefill = now;
            _tokens = Math.Min(_burst, _tokens + (elapsed.TotalSeconds * _permitsPerSecond));

            if (_tokens < 1)
            {
                return false;
            }

            _tokens -= 1;
            return true;
        }
    }
}
