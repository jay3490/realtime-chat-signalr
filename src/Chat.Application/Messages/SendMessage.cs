using Chat.Application.Abstractions;
using Chat.Application.Common;
using Chat.Application.Persistence;
using Chat.Domain.Messages;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Messages;

public sealed record SendMessageCommand(Guid RoomId, string Sender, string Body) : ICommand<MessageDto>;

internal sealed class SendMessageHandler(IChatDbContext db, TimeProvider clock)
    : ICommandHandler<SendMessageCommand, MessageDto>
{
    public async Task<MessageDto> HandleAsync(SendMessageCommand command, CancellationToken cancellationToken)
    {
        // Only the sender's membership (to check the invariant) and those of anyone
        // mentioned (to resolve the mentions) are needed, so the rest are not loaded.
        var mentioned = Mentions.Find(command.Body).ToArray();
        var room = await db.Rooms
            .Include(r => r.Members.Where(m => m.UserName == command.Sender || mentioned.Contains(m.UserName)))
            .SingleOrDefaultAsync(r => r.Id == command.RoomId, cancellationToken)
            ?? throw new NotFoundException("Room not found.");

        if (!room.HasMember(command.Sender))
        {
            throw new ForbiddenException("Join the room before posting to it.");
        }

        var message = room.Post(command.Sender, command.Body, clock.GetUtcNow());
        db.Messages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        return MessageDto.From(message);
    }
}
