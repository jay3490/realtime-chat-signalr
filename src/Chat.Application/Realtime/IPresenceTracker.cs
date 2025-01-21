namespace Chat.Application.Realtime;

public readonly record struct UserConnection(string UserName, string ConnectionId);

/// <summary>
/// Tracks which users are online across every API instance. A user is online while they
/// have at least one live connection, so a second tab or device does not flip them
/// offline when the first one closes.
/// </summary>
/// <remarks>
/// Each connection holds a lease that its instance keeps renewing. If an instance dies
/// without running its disconnect handlers, its connections simply stop being renewed and
/// <see cref="RemoveExpiredAsync"/> takes them out.
/// </remarks>
public interface IPresenceTracker
{
    /// <summary>Registers a connection. Returns true if the user has just come online.</summary>
    Task<bool> ConnectAsync(string userName, string connectionId, CancellationToken cancellationToken = default);

    /// <summary>Removes a connection. Returns true if it was the user's last one and they are now offline.</summary>
    Task<bool> DisconnectAsync(string userName, string connectionId, CancellationToken cancellationToken = default);

    /// <summary>Extends the lease of connections that are still open. Unknown connections are not re-added.</summary>
    Task RenewAsync(IReadOnlyCollection<UserConnection> connections, CancellationToken cancellationToken = default);

    /// <summary>Drops connections whose lease has run out and returns the users who went offline as a result.</summary>
    Task<IReadOnlyList<string>> RemoveExpiredAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetOnlineUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>Connection ids for a user on any instance; used to add all their devices to a new room's group.</summary>
    Task<IReadOnlyList<string>> GetConnectionsAsync(string userName, CancellationToken cancellationToken = default);
}

public sealed class PresenceOptions
{
    public const string SectionName = "Presence";

    /// <summary>How long a connection counts as alive without being renewed.</summary>
    public int LeaseSeconds { get; set; } = 90;

    /// <summary>How often each instance renews its connections and sweeps expired ones.</summary>
    public int RenewIntervalSeconds { get; set; } = 30;

    public TimeSpan Lease => TimeSpan.FromSeconds(LeaseSeconds);

    public TimeSpan RenewInterval => TimeSpan.FromSeconds(RenewIntervalSeconds);
}
