using Chat.Application.Abstractions;
using Chat.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Messages.Queries;

/// <summary>How far one member of a room has read.</summary>
public sealed record ReadMarkerDto(string UserName, long LastReadMessageId, DateTimeOffset? ReadAt);

/// <summary>Read markers for every member of a room; the client uses them to render "seen by".</summary>
public sealed record GetReadMarkersQuery(Guid RoomId, string UserName) : IQuery<IReadOnlyList<ReadMarkerDto>>;

internal sealed class GetReadMarkersHandler(IChatDbContext db, IChatReadDbContext read)
    : IQueryHandler<GetReadMarkersQuery, IReadOnlyList<ReadMarkerDto>>
{
    public async Task<IReadOnlyList<ReadMarkerDto>> HandleAsync(GetReadMarkersQuery query, CancellationToken cancellationToken)
    {
        await db.EnsureMemberAsync(query.RoomId, query.UserName, cancellationToken);

        return await read.Members
            .Where(m => m.RoomId == query.RoomId)
            .OrderBy(m => m.UserName)
            .Select(m => new ReadMarkerDto(m.UserName, m.LastReadMessageId, m.LastReadAt))
            .ToListAsync(cancellationToken);
    }
}
