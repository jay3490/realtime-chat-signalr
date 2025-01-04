using System.Text.RegularExpressions;
using Chat.Domain.Common;
using Chat.Domain.Messages;

namespace Chat.Domain.Rooms;

/// <summary>
/// A conversation: either a public channel or a direct conversation between two users.
/// The room owns its membership list and the members' read markers.
/// </summary>
public sealed partial class Room : AggregateRoot
{
    public const int MaxKeyLength = 80;
    public const int MaxNameLength = 32;

    private readonly List<RoomMember> _members = [];

    private Room()
    {
    }

    public Guid Id { get; private set; }

    public RoomKind Kind { get; private set; }

    /// <summary>
    /// Unique natural key. Prevents two channels with the same name and two direct
    /// conversations between the same pair of users.
    /// </summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>Channel name; null for direct conversations.</summary>
    public string? Name { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Members currently loaded with the aggregate. Handlers may load only the members
    /// they need, so this is not always the complete list.
    /// </summary>
    public IReadOnlyCollection<RoomMember> Members => _members;

    public static Room CreateChannel(string name, string createdBy, DateTimeOffset now)
    {
        var channelName = NormalizeChannelName(name);
        var room = new Room
        {
            Id = Guid.NewGuid(),
            Kind = RoomKind.Channel,
            Key = ChannelKey(channelName),
            Name = channelName,
            CreatedBy = createdBy,
            CreatedAt = now,
        };

        room._members.Add(new RoomMember(room.Id, createdBy, now));
        room.Raise(new RoomCreated(room.Id, room.Kind, room.Name, [createdBy]));
        return room;
    }

    public static Room CreateDirect(string userName, string otherUserName, DateTimeOffset now)
    {
        if (string.Equals(userName, otherUserName, StringComparison.Ordinal))
        {
            throw new DomainRuleException("A direct conversation needs two different users.");
        }

        var room = new Room
        {
            Id = Guid.NewGuid(),
            Kind = RoomKind.Direct,
            Key = DirectKey(userName, otherUserName),
            CreatedBy = userName,
            CreatedAt = now,
        };

        room._members.Add(new RoomMember(room.Id, userName, now));
        room._members.Add(new RoomMember(room.Id, otherUserName, now));
        room.Raise(new RoomCreated(room.Id, room.Kind, null, [userName, otherUserName]));
        return room;
    }

    public static string ChannelKey(string channelName) => $"channel:{channelName}";

    public static string DirectKey(string userName, string otherUserName)
    {
        var pair = new[] { userName, otherUserName };
        Array.Sort(pair, StringComparer.Ordinal);
        return $"direct:{pair[0]}|{pair[1]}";
    }

    public static string NormalizeChannelName(string? name)
    {
        var trimmed = name?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!ChannelNamePattern().IsMatch(trimmed))
        {
            throw new DomainRuleException(
                $"Channel names must be 2-{MaxNameLength} characters of lowercase letters, digits or '-'.");
        }

        return trimmed;
    }

    public bool HasMember(string userName) => FindMember(userName) is not null;

    public void Join(string userName, DateTimeOffset now)
    {
        if (Kind != RoomKind.Channel)
        {
            throw new DomainRuleException("Only channels can be joined.");
        }

        if (HasMember(userName))
        {
            return;
        }

        _members.Add(new RoomMember(Id, userName, now));
        Raise(new MemberJoined(Id, userName));
    }

    public Message Post(string sender, string body, DateTimeOffset now)
    {
        EnsureMember(sender);
        return Message.Create(Id, sender, body, now);
    }

    /// <summary>Advances the member's read marker; returns false if it was already at or past it.</summary>
    public bool MarkRead(string userName, long messageId, DateTimeOffset now)
    {
        var member = EnsureMember(userName);
        if (!member.AdvanceReadMarker(messageId, now))
        {
            return false;
        }

        Raise(new MessagesRead(Id, userName, messageId, now));
        return true;
    }

    private RoomMember? FindMember(string userName) =>
        _members.Find(m => string.Equals(m.UserName, userName, StringComparison.Ordinal));

    private RoomMember EnsureMember(string userName) =>
        FindMember(userName) ?? throw new DomainRuleException("Only members of a room can do that.");

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,31}$")]
    private static partial Regex ChannelNamePattern();
}
