using Chat.Application.Abstractions;
using Chat.Application.Common;
using Chat.Application.Persistence;
using Chat.Application.ReadModel;
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

/// <summary>A single room from the user's point of view, from the read model.</summary>
public sealed record GetRoomSummaryQuery(Guid RoomId, string UserName) : IQuery<RoomSummaryDto>;

/// <summary>
/// A single room from the user's point of view, read from the write model. Responses to
/// commands (create, join, open a direct conversation) use it, because they must reflect the
/// change even before the projection has applied it.
/// </summary>
public sealed record GetCurrentRoomSummaryQuery(Guid RoomId, string UserName) : IQuery<RoomSummaryDto>;

internal sealed class RoomSummaryHandlers(IChatReadDbContext read)
    : IQueryHandler<GetMyRoomsQuery, IReadOnlyList<RoomSummaryDto>>,
      IQueryHandler<GetRoomSummaryQuery, RoomSummaryDto>
{
    public async Task<IReadOnlyList<RoomSummaryDto>> HandleAsync(GetMyRoomsQuery query, CancellationToken cancellationToken)
    {
        var rooms = await Summaries(read.Members.Where(m => m.UserName == query.UserName), query.UserName)
            .ToListAsync(cancellationToken);

        return rooms
            .OrderByDescending(r => r.LastMessageId)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<RoomSummaryDto> HandleAsync(GetRoomSummaryQuery query, CancellationToken cancellationToken)
    {
        var memberships = read.Members.Where(m => m.RoomId == query.RoomId && m.UserName == query.UserName);
        return await Summaries(memberships, query.UserName).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Room not found.");
    }

    /// <summary>One SQL statement: counts and the latest message are stored on the rows by the projection.</summary>
    private IQueryable<RoomSummaryDto> Summaries(IQueryable<MemberView> memberships, string userName) =>
        from member in memberships
        join room in read.Rooms on member.RoomId equals room.Id
        select new RoomSummaryDto(
            room.Id,
            room.Kind,
            room.Kind == RoomKind.Channel
                ? room.Name!
                : read.Members
                    .Where(other => other.RoomId == room.Id && other.UserName != userName)
                    .Select(other => other.UserName)
                    .First(),
            member.UnreadCount,
            room.LastMessageId,
            member.LastReadMessageId);
}

internal sealed class CurrentRoomSummaryHandler(IChatDbContext db) : IQueryHandler<GetCurrentRoomSummaryQuery, RoomSummaryDto>
{
    public async Task<RoomSummaryDto> HandleAsync(GetCurrentRoomSummaryQuery query, CancellationToken cancellationToken)
    {
        var room = await db.Rooms
            .AsNoTracking()
            .Where(r => r.Id == query.RoomId)
            .Select(r => new
            {
                r.Kind,
                r.Name,
                LastRead = r.Members.Where(m => m.UserName == query.UserName).Select(m => (long?)m.LastReadMessageId).FirstOrDefault(),
                Other = r.Members.Where(m => m.UserName != query.UserName).Select(m => m.UserName).FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (room?.LastRead is not { } lastRead)
        {
            throw new NotFoundException("Room not found.");
        }

        var inRoom = db.Messages.Where(m => m.RoomId == query.RoomId);
        var unread = await inRoom.CountAsync(
            m => m.Id > lastRead && m.Sender != query.UserName && m.DeletedAt == null, cancellationToken);
        var lastMessageId = await inRoom.MaxAsync(m => (long?)m.Id, cancellationToken) ?? 0;

        var name = room.Kind == RoomKind.Channel ? room.Name! : room.Other ?? query.UserName;
        return new RoomSummaryDto(query.RoomId, room.Kind, name, unread, lastMessageId, lastRead);
    }
}
