using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Chat.Application.Realtime;

public sealed class TypingOptions
{
    public const string SectionName = "Typing";

    /// <summary>Minimum gap between two "is typing" broadcasts for the same user and room.</summary>
    public int ThrottleMilliseconds { get; set; } = 3000;

    /// <summary>
    /// How long clients show the indicator after a broadcast. Longer than the throttle so the
    /// indicator stays up while someone keeps typing, and disappears on its own if they stop
    /// or disconnect without saying so.
    /// </summary>
    public int IndicatorMilliseconds { get; set; } = 6000;
}

/// <summary>
/// Decides whether a keystroke notification should be broadcast. Clients may report every
/// keystroke; only the first one in each throttle window goes out. State is per instance,
/// which is enough because a connection always talks to the same instance.
/// </summary>
public sealed class TypingThrottle(TimeProvider clock, IOptions<TypingOptions> options)
{
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<(Guid RoomId, string UserName), DateTimeOffset> _lastBroadcast = new();
    private readonly TimeSpan _throttle = TimeSpan.FromMilliseconds(options.Value.ThrottleMilliseconds);
    private DateTimeOffset _lastPrune = clock.GetUtcNow();

    public TimeSpan IndicatorDuration { get; } = TimeSpan.FromMilliseconds(options.Value.IndicatorMilliseconds);

    /// <summary>Returns true if a typing notification should be sent now, and starts a new window if so.</summary>
    public bool TryBegin(Guid roomId, string userName)
    {
        var now = clock.GetUtcNow();
        PruneIfDue(now);

        var key = (roomId, userName);
        while (true)
        {
            if (!_lastBroadcast.TryGetValue(key, out var last))
            {
                if (_lastBroadcast.TryAdd(key, now))
                {
                    return true;
                }

                continue;
            }

            if (now - last < _throttle)
            {
                return false;
            }

            // Only one concurrent caller wins the new window.
            if (_lastBroadcast.TryUpdate(key, now, last))
            {
                return true;
            }
        }
    }

    /// <summary>Forgets the window, e.g. once the message has been sent, so the next keystroke is broadcast at once.</summary>
    public void Reset(Guid roomId, string userName) => _lastBroadcast.TryRemove((roomId, userName), out _);

    /// <summary>Number of (room, user) windows currently held; exposed for tests.</summary>
    public int TrackedCount => _lastBroadcast.Count;

    private void PruneIfDue(DateTimeOffset now)
    {
        if (now - _lastPrune < PruneInterval)
        {
            return;
        }

        _lastPrune = now;
        foreach (var (key, last) in _lastBroadcast)
        {
            if (now - last >= _throttle)
            {
                _lastBroadcast.TryRemove(new KeyValuePair<(Guid, string), DateTimeOffset>(key, last));
            }
        }
    }
}
