using Chat.Application.Realtime;
using Chat.Infrastructure.Presence;
using StackExchange.Redis;

namespace Chat.UnitTests.Realtime;

/// <summary>
/// Runs the presence contract against a real Redis server. Set <c>CHAT_TEST_REDIS</c> to a
/// connection string (for example <c>localhost:6379</c>) to enable it; CI provides Redis as
/// a service container. Each test uses its own key prefix and removes its keys afterwards.
/// </summary>
public sealed class RedisPresenceTrackerTests : PresenceTrackerContract, IAsyncLifetime
{
    public const string EnvironmentVariable = "CHAT_TEST_REDIS";

    private readonly string _prefix = $"chat-test:{Guid.NewGuid():N}:";
    private ConnectionMultiplexer? _redis;
    private RedisPresenceTracker? _tracker;

    protected override IPresenceTracker Tracker
    {
        get
        {
            Skip.If(_tracker is null, $"Set {EnvironmentVariable} to run the presence tests against Redis.");
            return _tracker!;
        }
    }

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        _redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
        _tracker = new RedisPresenceTracker(
            _redis,
            Clock,
            Microsoft.Extensions.Options.Options.Create(Options),
            Microsoft.Extensions.Options.Options.Create(new RedisOptions { KeyPrefix = _prefix }));
    }

    public async Task DisposeAsync()
    {
        if (_redis is null)
        {
            return;
        }

        var db = _redis.GetDatabase();
        foreach (var endpoint in _redis.GetEndPoints())
        {
            await foreach (var key in _redis.GetServer(endpoint).KeysAsync(pattern: _prefix + "*"))
            {
                await db.KeyDeleteAsync(key);
            }
        }

        await _redis.DisposeAsync();
    }
}
