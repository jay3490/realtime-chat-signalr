using Chat.Application.Abstractions;
using Chat.Application.Common;
using Chat.Application.Persistence;
using Chat.Domain.Rooms;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Rooms.Queries;

/// <summary>A room as seen by one member, including how much of it they have not read yet.</summary>
/// <param name="Name">The channel name, or the other participant for a direct conversation.</param>
/// <param name="UnreadCount">Messages from other people newer than the member's read marker.</param>
public sealed record RoomSummaryDto(
    Guid Id,
    RoomKind Kind,
    string Name,
    int UnreadCount,
    long LastMessageId,
    long LastReadMessageId);

/// <summary>Every room the user belongs to, most recently active first.</summary>
public sealed record GetMyRoomsQuery(string UserName) : IQuery<IReadOnlyList<RoomSummaryDto>>;

/// <summary>A single room from the user's point of view; used after a command creates or joins one.</summary>
public sealed record GetRoomSummaryQuery(Guid RoomId, string UserName) : IQuery<RoomSummaryDto>;

internal sealed class RoomSummaryHandlers(IChatReadDbContext read)
    : IQueryHandler<GetMyRoomsQuery, IReadOnlyList<RoomSummaryDto>>,
      IQueryHandler<GetRoomSummaryQuery, RoomSummaryDto>
{
    public async Task<IReadOnlyList<RoomSummaryDto>> HandleAsync(GetMyRoomsQuery query, CancellationToken cancellationToken)
    {
        var rooms = await Summaries(read.RoomMembers.Where(m => m.UserName == query.UserName), query.UserName)
            .ToListAsync(cancellationToken);

        return rooms
            .OrderByDescending(r => r.LastMessageId)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<RoomSummaryDto> HandleAsync(GetRoomSummaryQuery query, CancellationToken cancellationToken)
    {
        var memberships = read.RoomMembers.Where(m => m.RoomId == query.RoomId && m.UserName == query.UserName);
        return await Summaries(memberships, query.UserName).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Room not found.");
    }

    /// <summary>
    /// One SQL statement per call: unread counts and the latest message id are correlated
    /// subqueries served by the (RoomId, Id) index on Messages.
    /// </summary>
    private IQueryable<RoomSummaryDto> Summaries(IQueryable<RoomMember> memberships, string userName) =>
        from member in memberships
        join room in read.Rooms on member.RoomId equals room.Id
        select new RoomSummaryDto(
            room.Id,
            room.Kind,
            room.Kind == RoomKind.Channel
                ? room.Name!
                : read.RoomMembers
                    .Where(other => other.RoomId == room.Id && other.UserName != userName)
                    .Select(other => other.UserName)
                    .First(),
            read.Messages.Count(m => m.RoomId == room.Id && m.Id > member.LastReadMessageId && m.Sender != userName && m.DeletedAt == null),
            read.Messages.Where(m => m.RoomId == room.Id).Max(m => (long?)m.Id) ?? 0,
            member.LastReadMessageId);
}
