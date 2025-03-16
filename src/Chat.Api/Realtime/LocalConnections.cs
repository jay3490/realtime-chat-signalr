using System.Collections.Concurrent;
using Chat.Application.Realtime;
using Chat.Infrastructure.Observability;

namespace Chat.Api.Realtime;

/// <summary>Connections open on this instance, so their presence leases can be renewed.</summary>
public sealed class LocalConnections
{
    private readonly ConcurrentDictionary<string, string> _users = new(StringComparer.Ordinal);

    public LocalConnections(ChatMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        metrics.ObserveActiveConnections(() => _users.Count);
    }

    public void Add(string connectionId, string userName) => _users[connectionId] = userName;

    public void Remove(string connectionId) => _users.TryRemove(connectionId, out _);

    public IReadOnlyCollection<UserConnection> Snapshot() =>
        _users.Select(pair => new UserConnection(pair.Value, pair.Key)).ToList();
}
