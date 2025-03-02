using Chat.Application.Abstractions;
using Chat.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Messages.Queries;

/// <summary>How far one device of one member has received the room's messages.</summary>
public sealed record DeliveryMarkerDto(string UserName, string DeviceId, long LastDeliveredMessageId, DateTimeOffset DeliveredAt);

/// <summary>Per-device delivery markers for a room; the client uses them to render "delivered".</summary>
public sealed record GetDeliveryMarkersQuery(Guid RoomId, string UserName) : IQuery<IReadOnlyList<DeliveryMarkerDto>>;

internal sealed class GetDeliveryMarkersHandler(IChatDbContext db, IChatReadDbContext read)
    : IQueryHandler<GetDeliveryMarkersQuery, IReadOnlyList<DeliveryMarkerDto>>
{
    public async Task<IReadOnlyList<DeliveryMarkerDto>> HandleAsync(GetDeliveryMarkersQuery query, CancellationToken cancellationToken)
    {
        await db.EnsureMemberAsync(query.RoomId, query.UserName, cancellationToken);

        return await read.Deliveries
            .Where(d => d.RoomId == query.RoomId)
            .OrderBy(d => d.UserName)
            .ThenBy(d => d.DeviceId)
            .Select(d => new DeliveryMarkerDto(d.UserName, d.DeviceId, d.LastDeliveredMessageId, d.DeliveredAt))
            .ToListAsync(cancellationToken);
    }
}
