using Chat.Application.Realtime;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Chat.UnitTests.Realtime;

public class TypingThrottleTests
{
    private static readonly Guid Room = Guid.NewGuid();
    private static readonly Guid OtherRoom = Guid.NewGuid();

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly TypingThrottle _throttle;

    public TypingThrottleTests() =>
        _throttle = new TypingThrottle(_clock, Options.Create(new TypingOptions
        {
            ThrottleMilliseconds = 3000,
            IndicatorMilliseconds = 6000,
        }));

    [Fact]
    public void First_keystroke_is_broadcast()
    {
        Assert.True(_throttle.TryBegin(Room, "alice"));
    }

    [Fact]
    public void Keystrokes_inside_the_window_are_suppressed()
    {
        _throttle.TryBegin(Room, "alice");

        _clock.Advance(TimeSpan.FromMilliseconds(2999));

        Assert.False(_throttle.TryBegin(Room, "alice"));
    }

    [Fact]
    public void A_new_window_opens_once_the_throttle_has_passed()
    {
        _throttle.TryBegin(Room, "alice");

        _clock.Advance(TimeSpan.FromSeconds(3));

        Assert.True(_throttle.TryBegin(Room, "alice"));
        Assert.False(_throttle.TryBegin(Room, "alice"));
    }

    [Fact]
    public void Windows_are_per_user_and_per_room()
    {
        _throttle.TryBegin(Room, "alice");

        Assert.True(_throttle.TryBegin(Room, "bob"));
        Assert.True(_throttle.TryBegin(OtherRoom, "alice"));
    }

    [Fact]
    public void Reset_lets_the_next_keystroke_through_immediately()
    {
        _throttle.TryBegin(Room, "alice");

        _throttle.Reset(Room, "alice");

        Assert.True(_throttle.TryBegin(Room, "alice"));
    }

    [Fact]
    public void Indicator_outlives_the_throttle_window_so_it_does_not_flicker()
    {
        Assert.Equal(TimeSpan.FromSeconds(6), _throttle.IndicatorDuration);
    }

    [Fact]
    public void Stale_windows_are_pruned()
    {
        _throttle.TryBegin(Room, "alice");
        _throttle.TryBegin(Room, "bob");

        _clock.Advance(TimeSpan.FromMinutes(2));
        _throttle.TryBegin(OtherRoom, "carol");

        Assert.Equal(1, _throttle.TrackedCount);
    }

    [Fact]
    public void Concurrent_keystrokes_produce_one_broadcast_per_window()
    {
        var broadcasts = 0;

        Parallel.For(0, 1000, _ =>
        {
            if (_throttle.TryBegin(Room, "alice"))
            {
                Interlocked.Increment(ref broadcasts);
            }
        });

        Assert.Equal(1, broadcasts);
    }
}
