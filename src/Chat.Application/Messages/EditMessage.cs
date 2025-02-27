using Chat.Application.Abstractions;
using Chat.Application.Common;
using Chat.Application.Persistence;
using Chat.Domain.Messages;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Messages;

/// <summary>Changes the text of one of the user's own messages. Returns the message as it now stands.</summary>
public sealed record EditMessageCommand(Guid RoomId, long MessageId, string UserName, string Body) : ICommand<MessageDto>;

/// <summary>Deletes one of the user's own messages. Returns false if it was already deleted.</summary>
public sealed record DeleteMessageCommand(Guid RoomId, long MessageId, string UserName) : ICommand<bool>;

internal sealed class MessageChangeHandlers(IChatDbContext db, TimeProvider clock)
    : ICommandHandler<EditMessageCommand, MessageDto>,
      ICommandHandler<DeleteMessageCommand, bool>
{
    public async Task<MessageDto> HandleAsync(EditMessageCommand command, CancellationToken cancellationToken)
    {
        var message = await LoadOwnMessageAsync(command.RoomId, command.MessageId, command.UserName, "edit", cancellationToken);
        if (message.Edit(command.UserName, command.Body, clock.GetUtcNow()))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return MessageDto.From(message);
    }

    public async Task<bool> HandleAsync(DeleteMessageCommand command, CancellationToken cancellationToken)
    {
        var message = await LoadOwnMessageAsync(command.RoomId, command.MessageId, command.UserName, "delete", cancellationToken);
        if (!message.Delete(command.UserName, clock.GetUtcNow()))
        {
            return false;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<Message> LoadOwnMessageAsync(
        Guid roomId, long messageId, string userName, string action, CancellationToken cancellationToken)
    {
        // Membership is checked first so that non-members cannot tell which ids exist.
        await db.EnsureMemberAsync(roomId, userName, cancellationToken);

        var message = await db.Messages.SingleOrDefaultAsync(m => m.Id == messageId && m.RoomId == roomId, cancellationToken)
            ?? throw new NotFoundException("Message not found in this room.");
        if (!message.IsAuthor(userName))
        {
            throw new ForbiddenException($"You can only {action} your own messages.");
        }

        return message;
    }
}
