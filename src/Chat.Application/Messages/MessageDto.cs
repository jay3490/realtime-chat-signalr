using Chat.Domain.Messages;

namespace Chat.Application.Messages;

public sealed record MessageDto(long Id, Guid RoomId, string Sender, string Body, DateTimeOffset SentAt)
{
    public static MessageDto From(Message message) =>
        new(message.Id, message.RoomId, message.Sender, message.Body, message.SentAt);
}
