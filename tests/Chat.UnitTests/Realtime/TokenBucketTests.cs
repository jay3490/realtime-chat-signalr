using Chat.Application.Realtime;
using Microsoft.Extensions.Time.Testing;

namespace Chat.UnitTests.Realtime;

public class TokenBucketTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_full_bucket_allows_a_burst_and_then_refuses()
    {
        var bucket = new TokenBucket(_clock, permitsPerSecond: 1, burst: 3);

        Assert.Equal([true, true, true, false], Enumerable.Range(0, 4).Select(_ => bucket.TryTake()));
    }

    [Fact]
    public void Permits_come_back_at_the_configured_rate()
    {
        var bucket = new TokenBucket(_clock, permitsPerSecond: 2, burst: 2);
        bucket.TryTake();
        bucket.TryTake();

        _clock.Advance(TimeSpan.FromMilliseconds(499));
        Assert.False(bucket.TryTake());

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(bucket.TryTake());
        Assert.False(bucket.TryTake());
    }

    [Fact]
    public void An_idle_bucket_never_holds_more_than_the_burst()
    {
        var bucket = new TokenBucket(_clock, permitsPerSecond: 10, burst: 2);

        _clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal([true, true, false], Enumerable.Range(0, 3).Select(_ => bucket.TryTake()));
    }

    [Fact]
    public void Concurrent_callers_cannot_take_more_than_the_burst()
    {
        var bucket = new TokenBucket(_clock, permitsPerSecond: 1, burst: 50);
        var taken = 0;

        Parallel.For(0, 500, _ =>
        {
            if (bucket.TryTake())
            {
                Interlocked.Increment(ref taken);
            }
        });

        Assert.Equal(50, taken);
    }

    [Fact]
    public void Nonsensical_limits_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TokenBucket(_clock, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TokenBucket(_clock, 1, 0));
    }
}
