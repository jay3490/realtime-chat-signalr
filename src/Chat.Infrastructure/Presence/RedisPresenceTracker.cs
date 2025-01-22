using Chat.Application.Realtime;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Chat.Infrastructure.Presence;

/// <summary>
/// Presence shared by every API instance.
/// </summary>
/// <remarks>
/// Layout: a set <c>{prefix}presence:online</c> of user names, and per user a sorted set
/// <c>{prefix}presence:user:{name}</c> of connection ids scored by lease expiry (Unix ms).
/// Every check-and-update runs as a Lua script so that two instances handling the same
/// user's connections at the same moment cannot both announce (or both miss) a change.
/// </remarks>
public sealed class RedisPresenceTracker(
    IConnectionMultiplexer redis,
    TimeProvider clock,
    IOptions<PresenceOptions> presenceOptions,
    IOptions<RedisOptions> redisOptions) : IPresenceTracker
{
    // KEYS: user set, online set. ARGV: user, connection, now, expiry, lease ms.
    // Returns 1 if the user was not online before.
    private const string ConnectScript = """
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[3])
        redis.call('ZADD', KEYS[1], ARGV[4], ARGV[2])
        redis.call('PEXPIRE', KEYS[1], ARGV[5])
        return redis.call('SADD', KEYS[2], ARGV[1])
        """;

    // KEYS: user set, online set. ARGV: user, connection (may be empty), now.
    // Removes the connection and any expired ones; returns 1 if the user just went offline.
    private const string ReleaseScript = """
        if ARGV[2] ~= '' then
          redis.call('ZREM', KEYS[1], ARGV[2])
        end
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[3])
        if redis.call('ZCARD', KEYS[1]) > 0 then
          return 0
        end
        redis.call('DEL', KEYS[1])
        return redis.call('SREM', KEYS[2], ARGV[1])
        """;

    private readonly TimeSpan _lease = presenceOptions.Value.Lease;
    private readonly string _prefix = redisOptions.Value.KeyPrefix;

    private IDatabase Db => redis.GetDatabase();

    private RedisKey OnlineKey => $"{_prefix}presence:online";

    public async Task<bool> ConnectAsync(string userName, string connectionId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var result = await Db.ScriptEvaluateAsync(
            ConnectScript,
            [UserKey(userName), OnlineKey],
            [userName, connectionId, ToScore(now), ToScore(now + _lease), (long)(_lease.TotalMilliseconds * 2)]);
        return (long)result == 1;
    }

    public Task<bool> DisconnectAsync(string userName, string connectionId, CancellationToken cancellationToken = default) =>
        ReleaseAsync(userName, connectionId);

    public async Task RenewAsync(IReadOnlyCollection<UserConnection> connections, CancellationToken cancellationToken = default)
    {
        if (connections.Count == 0)
        {
            return;
        }

        var expiresAt = ToScore(clock.GetUtcNow() + _lease);
        var keyTtl = _lease * 2;
        var batch = Db.CreateBatch();
        var pending = new List<Task>(connections.Count * 2);
        foreach (var (userName, connectionId) in connections)
        {
            var key = UserKey(userName);

            // "Exists" so a connection that was closed (or expired) elsewhere is not brought back.
            pending.Add(batch.SortedSetAddAsync(key, connectionId, expiresAt, SortedSetWhen.Exists));
            pending.Add(batch.KeyExpireAsync(key, keyTtl));
        }

        batch.Execute();
        await Task.WhenAll(pending);
    }

    public async Task<IReadOnlyList<string>> RemoveExpiredAsync(CancellationToken cancellationToken = default)
    {
        var wentOffline = new List<string>();
        foreach (var member in await Db.SetMembersAsync(OnlineKey))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var userName = member.ToString();

            // Each instance sweeps; SREM inside the script ensures only one reports the user.
            if (await ReleaseAsync(userName, connectionId: string.Empty))
            {
                wentOffline.Add(userName);
            }
        }

        return wentOffline;
    }

    public async Task<IReadOnlyList<string>> GetOnlineUsersAsync(CancellationToken cancellationToken = default)
    {
        var members = await Db.SetMembersAsync(OnlineKey);
        return members.Select(m => m.ToString()).Order(StringComparer.Ordinal).ToList();
    }

    public async Task<IReadOnlyList<string>> GetConnectionsAsync(string userName, CancellationToken cancellationToken = default)
    {
        var live = await Db.SortedSetRangeByScoreAsync(
            UserKey(userName), ToScore(clock.GetUtcNow()), double.PositiveInfinity, Exclude.Start);
        return live.Select(c => c.ToString()).ToList();
    }

    private async Task<bool> ReleaseAsync(string userName, string connectionId)
    {
        var result = await Db.ScriptEvaluateAsync(
            ReleaseScript,
            [UserKey(userName), OnlineKey],
            [userName, connectionId, ToScore(clock.GetUtcNow())]);
        return (long)result == 1;
    }

    private RedisKey UserKey(string userName) => $"{_prefix}presence:user:{userName}";

    private static long ToScore(DateTimeOffset time) => time.ToUnixTimeMilliseconds();
}
