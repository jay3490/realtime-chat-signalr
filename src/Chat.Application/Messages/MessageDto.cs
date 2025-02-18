using Chat.Domain.Messages;

namespace Chat.Application.Messages;

/// <param name="Body">The current text; empty when <paramref name="IsDeleted"/>.</param>
/// <param name="Revision">1 for the text as sent, incremented by every edit.</param>
public sealed record MessageDto(
    long Id,
    Guid RoomId,
    string Sender,
    string Body,
    DateTimeOffset SentAt,
    int Revision,
    DateTimeOffset? EditedAt,
    bool IsDeleted)
{
    public static MessageDto From(Message message) =>
        new(message.Id, message.RoomId, message.Sender, message.Body, message.SentAt,
            message.Revision, message.EditedAt, message.IsDeleted);
}

public sealed record MessageDeletedDto(Guid RoomId, long MessageId, DateTimeOffset DeletedAt);
