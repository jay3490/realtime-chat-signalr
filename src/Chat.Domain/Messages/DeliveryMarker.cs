using System.Text.RegularExpressions;
using Chat.Domain.Common;

namespace Chat.Domain.Messages;

/// <summary>
/// How far one of a member's devices has received a room's messages. Each device reports
/// separately, so the sender can tell "delivered to a phone" from "seen".
/// </summary>
public sealed partial class DeliveryMarker : AggregateRoot
{
    public const int MaxDeviceIdLength = 64;

    private DeliveryMarker()
    {
    }

    public Guid RoomId { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    /// <summary>Chosen by the client and stable per browser or app install.</summary>
    public string DeviceId { get; private set; } = string.Empty;

    public long LastDeliveredMessageId { get; private set; }

    public DateTimeOffset DeliveredAt { get; private set; }

    public static bool IsValidDeviceId(string? deviceId) => deviceId is not null && DeviceIdPattern().IsMatch(deviceId);

    public static DeliveryMarker Start(Guid roomId, string userName, string deviceId)
    {
        if (!IsValidDeviceId(deviceId))
        {
            throw new DomainRuleException($"Device ids must be 1-{MaxDeviceIdLength} characters of letters, digits, '-' or '_'.");
        }

        return new DeliveryMarker { RoomId = roomId, UserName = userName, DeviceId = deviceId };
    }

    /// <summary>Moves the marker forward; a report for an older message is ignored.</summary>
    public bool Advance(long messageId, DateTimeOffset now)
    {
        if (messageId <= LastDeliveredMessageId)
        {
            return false;
        }

        LastDeliveredMessageId = messageId;
        DeliveredAt = now;
        Raise(new MessagesDelivered(RoomId, UserName, DeviceId, messageId, now));
        return true;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex DeviceIdPattern();
}

public sealed record MessagesDelivered(
    Guid RoomId,
    string UserName,
    string DeviceId,
    long LastDeliveredMessageId,
    DateTimeOffset DeliveredAt) : IDomainEvent;
