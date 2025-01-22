namespace Chat.Infrastructure.Presence;

/// <summary>
/// Redis settings. Redis is used only when <c>ConnectionStrings:Redis</c> is set; without
/// it the API runs as a single instance with in-memory presence and no backplane.
/// </summary>
public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>
    /// Use Redis as the SignalR backplane so that messages sent on one instance reach
    /// clients connected to the others.
    /// </summary>
    public bool Backplane { get; set; } = true;

    /// <summary>Prefix for presence keys and backplane channels, so several apps can share one Redis.</summary>
    public string KeyPrefix { get; set; } = "chat:";
}
