using Chat.Application.Abstractions;
using Chat.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Rooms.Queries;

/// <summary>Ids of the rooms a user belongs to; the hub joins these groups on connect.</summary>
public sealed record GetMyRoomIdsQuery(string UserName) : IQuery<IReadOnlyList<Guid>>;

public sealed record IsRoomMemberQuery(Guid RoomId, string UserName) : IQuery<bool>;

internal sealed class RoomMembershipHandlers(IChatReadDbContext read)
    : IQueryHandler<GetMyRoomIdsQuery, IReadOnlyList<Guid>>,
      IQueryHandler<IsRoomMemberQuery, bool>
{
    public async Task<IReadOnlyList<Guid>> HandleAsync(GetMyRoomIdsQuery query, CancellationToken cancellationToken) =>
        await read.RoomMembers
            .Where(m => m.UserName == query.UserName)
            .Select(m => m.RoomId)
            .ToListAsync(cancellationToken);

    public Task<bool> HandleAsync(IsRoomMemberQuery query, CancellationToken cancellationToken) =>
        read.RoomMembers.AnyAsync(m => m.RoomId == query.RoomId && m.UserName == query.UserName, cancellationToken);
}
