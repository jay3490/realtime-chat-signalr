using Chat.Domain.Common;

namespace Chat.Domain.Messages;

public sealed class Message : AggregateRoot
{
    public const int MaxBodyLength = 4000;

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

    public string Body { get; private set; } = string.Empty;

    public DateTimeOffset SentAt { get; private set; }

    internal static Message Create(Guid roomId, string sender, string body, IReadOnlyList<string> mentions, DateTimeOffset now)
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

        var message = new Message
        {
            RoomId = roomId,
            Sender = sender,
            Body = text,
            SentAt = now,
        };

        // The id is assigned by the database, so the event carries the entity itself and
        // is only dispatched after the insert has completed.
        message.Raise(new MessagePosted(message, mentions));
        return message;
    }
}
