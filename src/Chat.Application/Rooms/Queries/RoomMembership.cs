using Chat.Application.Abstractions;
using Chat.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Rooms.Queries;

/// <summary>Ids of the rooms a user belongs to; the hub joins these groups on connect.</summary>
public sealed record GetMyRoomIdsQuery(string UserName) : IQuery<IReadOnlyList<Guid>>;

public sealed record IsRoomMemberQuery(Guid RoomId, string UserName) : IQuery<bool>;

/// <summary>
/// Answered from the write model: a connection that missed a room's group because the read
/// model had not caught up with a join would silently miss that room's messages.
/// </summary>
internal sealed class RoomMembershipHandlers(IChatDbContext db)
    : IQueryHandler<GetMyRoomIdsQuery, IReadOnlyList<Guid>>,
      IQueryHandler<IsRoomMemberQuery, bool>
{
    public async Task<IReadOnlyList<Guid>> HandleAsync(GetMyRoomIdsQuery query, CancellationToken cancellationToken) =>
        await db.Rooms
            .SelectMany(r => r.Members)
            .Where(m => m.UserName == query.UserName)
            .Select(m => m.RoomId)
            .ToListAsync(cancellationToken);

    public Task<bool> HandleAsync(IsRoomMemberQuery query, CancellationToken cancellationToken) =>
        db.IsMemberAsync(query.RoomId, query.UserName, cancellationToken);
}
