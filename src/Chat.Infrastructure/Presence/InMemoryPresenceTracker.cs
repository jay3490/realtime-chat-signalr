using Chat.Application.Realtime;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.Presence;

/// <summary>
/// Presence for a single instance. Same semantics as the Redis tracker, but state lives in
/// process memory, so it is only correct when one API instance is running.
/// </summary>
public sealed class InMemoryPresenceTracker(TimeProvider clock, IOptions<PresenceOptions> options) : IPresenceTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<string, DateTimeOffset>> _connections = new(StringComparer.Ordinal);
    private readonly TimeSpan _lease = options.Value.Lease;

    public Task<bool> ConnectAsync(string userName, string connectionId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            var cameOnline = !_connections.TryGetValue(userName, out var leases);
            if (leases is null)
            {
                leases = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
                _connections[userName] = leases;
            }

            leases[connectionId] = now + _lease;
            return Task.FromResult(cameOnline);
        }
    }

    public Task<bool> DisconnectAsync(string userName, string connectionId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(userName, out var leases))
            {
                return Task.FromResult(false);
            }

            leases.Remove(connectionId);
            RemoveExpired(leases, clock.GetUtcNow());
            if (leases.Count > 0)
            {
                return Task.FromResult(false);
            }

            _connections.Remove(userName);
            return Task.FromResult(true);
        }
    }

    public Task RenewAsync(IReadOnlyCollection<UserConnection> connections, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var expiresAt = clock.GetUtcNow() + _lease;
            foreach (var (userName, connectionId) in connections)
            {
                if (_connections.TryGetValue(userName, out var leases) && leases.ContainsKey(connectionId))
                {
                    leases[connectionId] = expiresAt;
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> RemoveExpiredAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            var wentOffline = new List<string>();
            foreach (var (userName, leases) in _connections)
            {
                RemoveExpired(leases, now);
                if (leases.Count == 0)
                {
                    wentOffline.Add(userName);
                }
            }

            wentOffline.ForEach(u => _connections.Remove(u));
            return Task.FromResult<IReadOnlyList<string>>(wentOffline);
        }
    }

    public Task<IReadOnlyList<string>> GetOnlineUsersAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<string>>(_connections.Keys.Order(StringComparer.Ordinal).ToList());
        }
    }

    public Task<IReadOnlyList<string>> GetConnectionsAsync(string userName, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<string> connections = _connections.TryGetValue(userName, out var leases)
                ? leases.Keys.ToList()
                : [];
            return Task.FromResult(connections);
        }
    }

    private static void RemoveExpired(Dictionary<string, DateTimeOffset> leases, DateTimeOffset now)
    {
        foreach (var (connectionId, expiresAt) in leases)
        {
            if (expiresAt <= now)
            {
                leases.Remove(connectionId);
            }
        }
    }
}
