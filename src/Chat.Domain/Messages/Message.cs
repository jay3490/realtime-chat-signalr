using Chat.Domain.Common;

namespace Chat.Domain.Messages;

public sealed class Message : AggregateRoot
{
    public const int MaxBodyLength = 4000;

    /// <summary>How long after sending the author may still change the text.</summary>
    public static readonly TimeSpan EditWindow = TimeSpan.FromHours(24);

    private Message()
    {
    }

    /// <summary>
    /// Database-generated and monotonically increasing, so it doubles as the paging
    /// cursor and as the position for read markers.
    /// </summary>
    public long Id { get; private set; }

    public Guid RoomId { get; private set; }

    public string Sender { get; private set; } = string.Empty;

    /// <summary>The current text; empty once the message has been deleted.</summary>
    public string Body { get; private set; } = string.Empty;

    public DateTimeOffset SentAt { get; private set; }

    /// <summary>1 for the text as sent, incremented by every edit.</summary>
    public int Revision { get; private set; } = 1;

    public DateTimeOffset? EditedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    internal static Message Create(Guid roomId, string sender, string body, IReadOnlyList<string> mentions, DateTimeOffset now)
    {
        var message = new Message
        {
            RoomId = roomId,
            Sender = sender,
            Body = ValidBody(body),
            SentAt = now,
        };

        // The id is assigned by the database, so the event carries the entity itself and
        // is only dispatched after the insert has completed.
        message.Raise(new MessagePosted(message, mentions));
        return message;
    }

    public bool IsAuthor(string userName) => string.Equals(Sender, userName, StringComparison.Ordinal);

    /// <summary>
    /// Replaces the text. Only the author can edit, only within <see cref="EditWindow"/> and
    /// not once deleted. Returns false when the new text is the same as the current one.
    /// </summary>
    public bool Edit(string userName, string body, DateTimeOffset now)
    {
        EnsureAuthor(userName, "Only the author can edit a message.");
        if (IsDeleted)
        {
            throw new DomainRuleException("A deleted message cannot be edited.");
        }

        if (now - SentAt > EditWindow)
        {
            throw new DomainRuleException($"Messages can only be edited for {EditWindow.TotalHours:0} hours after they are sent.");
        }

        var text = ValidBody(body);
        if (string.Equals(text, Body, StringComparison.Ordinal))
        {
            return false;
        }

        Body = text;
        Revision++;
        EditedAt = now;
        Raise(new MessageEdited(this));
        return true;
    }

    /// <summary>
    /// Deletes the message, leaving a tombstone so its place in the history and in read
    /// markers stays valid. Only the author can delete; deleting twice is a no-op.
    /// </summary>
    public bool Delete(string userName, DateTimeOffset now)
    {
        EnsureAuthor(userName, "Only the author can delete a message.");
        if (IsDeleted)
        {
            return false;
        }

        Body = string.Empty;
        DeletedAt = now;
        Raise(new MessageDeleted(this));
        return true;
    }

    private void EnsureAuthor(string userName, string error)
    {
        if (!IsAuthor(userName))
        {
            throw new DomainRuleException(error);
        }
    }

    private static string ValidBody(string? body)
    {
        var text = body?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            throw new DomainRuleException("A message cannot be empty.");
        }

        if (text.Length > MaxBodyLength)
        {
            throw new DomainRuleException($"A message cannot be longer than {MaxBodyLength} characters.");
        }

        return text;
    }
}
