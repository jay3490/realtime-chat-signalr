using Chat.Application.Realtime;
using Microsoft.Extensions.Time.Testing;

namespace Chat.UnitTests.Realtime;

/// <summary>
/// Behaviour every presence tracker must have. Each implementation runs the same tests,
/// which keeps the single-instance tracker and the Redis tracker interchangeable.
/// </summary>
public abstract class PresenceTrackerContract
{
    protected static readonly PresenceOptions Options = new() { LeaseSeconds = 90, RenewIntervalSeconds = 30 };

    protected FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));

    protected abstract IPresenceTracker Tracker { get; }

    [Fact]
    public async Task First_connection_brings_a_user_online()
    {
        Assert.True(await Tracker.ConnectAsync("alice", "c1"));
        Assert.Equal(["alice"], await Tracker.GetOnlineUsersAsync());
    }

    [Fact]
    public async Task Further_connections_do_not_announce_the_user_again()
    {
        await Tracker.ConnectAsync("alice", "phone");

        Assert.False(await Tracker.ConnectAsync("alice", "laptop"));
        Assert.Equal(["laptop", "phone"], (await Tracker.GetConnectionsAsync("alice")).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Closing_one_of_several_connections_keeps_the_user_online()
    {
        await Tracker.ConnectAsync("alice", "phone");
        await Tracker.ConnectAsync("alice", "laptop");

        Assert.False(await Tracker.DisconnectAsync("alice", "phone"));
        Assert.Equal(["alice"], await Tracker.GetOnlineUsersAsync());
        Assert.Equal(["laptop"], await Tracker.GetConnectionsAsync("alice"));
    }

    [Fact]
    public async Task Closing_the_last_connection_takes_the_user_offline()
    {
        await Tracker.ConnectAsync("alice", "phone");
        await Tracker.ConnectAsync("alice", "laptop");
        await Tracker.DisconnectAsync("alice", "phone");

        Assert.True(await Tracker.DisconnectAsync("alice", "laptop"));
        Assert.Empty(await Tracker.GetOnlineUsersAsync());
        Assert.Empty(await Tracker.GetConnectionsAsync("alice"));
    }

    [Fact]
    public async Task Disconnecting_an_unknown_connection_reports_nothing()
    {
        Assert.False(await Tracker.DisconnectAsync("alice", "never-connected"));
    }

    [Fact]
    public async Task Users_are_tracked_independently()
    {
        await Tracker.ConnectAsync("alice", "a1");
        await Tracker.ConnectAsync("bob", "b1");

        await Tracker.DisconnectAsync("alice", "a1");

        Assert.Equal(["bob"], await Tracker.GetOnlineUsersAsync());
    }

    [Fact]
    public async Task Connections_that_stop_being_renewed_expire()
    {
        await Tracker.ConnectAsync("alice", "on-crashed-instance");
        await Tracker.ConnectAsync("bob", "b1");

        Clock.Advance(Options.Lease - TimeSpan.FromSeconds(1));
        await Tracker.RenewAsync([new UserConnection("bob", "b1")]);
        Assert.Empty(await Tracker.RemoveExpiredAsync());

        Clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(["alice"], await Tracker.RemoveExpiredAsync());
        Assert.Equal(["bob"], await Tracker.GetOnlineUsersAsync());
    }

    [Fact]
    public async Task An_expired_connection_does_not_keep_a_user_online_after_their_live_one_closes()
    {
        await Tracker.ConnectAsync("alice", "stale");
        Clock.Advance(Options.Lease + TimeSpan.FromSeconds(1));
        await Tracker.ConnectAsync("alice", "fresh");

        Assert.True(await Tracker.DisconnectAsync("alice", "fresh"));
    }

    [Fact]
    public async Task Renewing_does_not_resurrect_a_closed_connection()
    {
        await Tracker.ConnectAsync("alice", "c1");
        await Tracker.DisconnectAsync("alice", "c1");

        await Tracker.RenewAsync([new UserConnection("alice", "c1")]);

        Assert.Empty(await Tracker.GetOnlineUsersAsync());
    }

    [Fact]
    public async Task Expiry_reports_each_user_once()
    {
        await Tracker.ConnectAsync("alice", "c1");
        Clock.Advance(Options.Lease + TimeSpan.FromSeconds(1));

        Assert.Equal(["alice"], await Tracker.RemoveExpiredAsync());
        Assert.Empty(await Tracker.RemoveExpiredAsync());
    }
}
