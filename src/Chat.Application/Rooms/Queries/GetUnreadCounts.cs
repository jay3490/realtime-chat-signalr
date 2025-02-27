using Chat.Application.Abstractions;
using Chat.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Rooms.Queries;

public sealed record UnreadCountDto(Guid RoomId, int UnreadCount, long LastReadMessageId);

/// <summary>
/// Unread counts for every room the user is in. The projection keeps the counts up to date,
/// so this is a lookup by user name rather than a count per room.
/// </summary>
public sealed record GetUnreadCountsQuery(string UserName) : IQuery<IReadOnlyList<UnreadCountDto>>;

internal sealed class GetUnreadCountsHandler(IChatReadDbContext read)
    : IQueryHandler<GetUnreadCountsQuery, IReadOnlyList<UnreadCountDto>>
{
    public async Task<IReadOnlyList<UnreadCountDto>> HandleAsync(GetUnreadCountsQuery query, CancellationToken cancellationToken) =>
        await read.Members
            .Where(member => member.UserName == query.UserName)
            .Select(member => new UnreadCountDto(member.RoomId, member.UnreadCount, member.LastReadMessageId))
            .ToListAsync(cancellationToken);
}
