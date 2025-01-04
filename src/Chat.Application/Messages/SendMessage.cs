using Chat.Application.Abstractions;
using Chat.Application.Common;
using Chat.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Messages;

public sealed record SendMessageCommand(Guid RoomId, string Sender, string Body) : ICommand<MessageDto>;

internal sealed class SendMessageHandler(IChatDbContext db, TimeProvider clock)
    : ICommandHandler<SendMessageCommand, MessageDto>
{
    public async Task<MessageDto> HandleAsync(SendMessageCommand command, CancellationToken cancellationToken)
    {
        // Only the sender's membership is needed to check the invariant, so the rest of
        // the member list is not loaded.
        var room = await db.Rooms
            .Include(r => r.Members.Where(m => m.UserName == command.Sender))
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
