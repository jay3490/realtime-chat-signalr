using Chat.Application.Abstractions;
using Chat.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Rooms.Queries;

public sealed record UnreadCountDto(Guid RoomId, int UnreadCount, long LastReadMessageId);

/// <summary>
/// Unread counts for every room the user is in. Cheaper than the full room list; the
/// client calls it after reconnecting to correct badges for messages it may have missed.
/// </summary>
public sealed record GetUnreadCountsQuery(string UserName) : IQuery<IReadOnlyList<UnreadCountDto>>;

internal sealed class GetUnreadCountsHandler(IChatReadDbContext read)
    : IQueryHandler<GetUnreadCountsQuery, IReadOnlyList<UnreadCountDto>>
{
    public async Task<IReadOnlyList<UnreadCountDto>> HandleAsync(GetUnreadCountsQuery query, CancellationToken cancellationToken) =>
        await read.RoomMembers
            .Where(member => member.UserName == query.UserName)
            .Select(member => new UnreadCountDto(
                member.RoomId,
                read.Messages.Count(m =>
                    m.RoomId == member.RoomId && m.Id > member.LastReadMessageId && m.Sender != query.UserName),
                member.LastReadMessageId))
            .ToListAsync(cancellationToken);
}
