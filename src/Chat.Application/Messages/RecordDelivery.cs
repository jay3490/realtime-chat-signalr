using Chat.Application.Abstractions;
using Chat.Application.Common;
using Chat.Application.Persistence;
using Chat.Domain.Messages;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Messages;

/// <summary>
/// Records that one of the user's devices has received every message in the room up to and
/// including <paramref name="MessageId"/>. Returns false when the device had already reported it.
/// </summary>
public sealed record RecordDeliveryCommand(Guid RoomId, string UserName, string DeviceId, long MessageId) : ICommand<bool>;

internal sealed class RecordDeliveryHandler(IChatDbContext db, TimeProvider clock) : ICommandHandler<RecordDeliveryCommand, bool>
{
    public async Task<bool> HandleAsync(RecordDeliveryCommand command, CancellationToken cancellationToken)
    {
        if (!DeliveryMarker.IsValidDeviceId(command.DeviceId))
        {
            throw new RequestValidationException("The device id is missing or malformed.");
        }

        await db.EnsureMemberAsync(command.RoomId, command.UserName, cancellationToken);

        if (!await db.Messages.AnyAsync(m => m.Id == command.MessageId && m.RoomId == command.RoomId, cancellationToken))
        {
            throw new NotFoundException("Message not found in this room.");
        }

        var marker = await db.DeliveryMarkers.SingleOrDefaultAsync(
            m => m.RoomId == command.RoomId && m.UserName == command.UserName && m.DeviceId == command.DeviceId,
            cancellationToken);
        if (marker is null)
        {
            marker = DeliveryMarker.Start(command.RoomId, command.UserName, command.DeviceId);
            db.DeliveryMarkers.Add(marker);
        }

        if (!marker.Advance(command.MessageId, clock.GetUtcNow()))
        {
            return false;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            // The same device reported twice at once (for example from two tabs sharing an id).
            db.DiscardChanges();
            return false;
        }
    }
}
