using Chat.Application.Abstractions;
using Chat.Application.Common;
using Chat.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Messages.Queries;

/// <summary>A page of messages in ascending id order.</summary>
/// <param name="HasMore">True when more messages exist beyond this page in the direction requested.</param>
public sealed record MessagePage(IReadOnlyList<MessageDto> Messages, bool HasMore);

/// <summary>
/// Keyset-paged history. With <paramref name="Before"/> (or neither cursor) it pages
/// backwards from the newest message; with <paramref name="After"/> it pages forwards.
/// Deleted messages are returned as tombstones so clients can remove them from view.
/// </summary>
public sealed record GetMessageHistoryQuery(
    Guid RoomId,
    string UserName,
    long? Before = null,
    long? After = null,
    int Limit = GetMessageHistoryQuery.DefaultLimit) : IQuery<MessagePage>
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;
}

internal sealed class GetMessageHistoryHandler(IChatDbContext db, IChatReadDbContext read)
    : IQueryHandler<GetMessageHistoryQuery, MessagePage>
{
    public async Task<MessagePage> HandleAsync(GetMessageHistoryQuery query, CancellationToken cancellationToken)
    {
        if (query.Before is not null && query.After is not null)
        {
            throw new RequestValidationException("Use either 'before' or 'after', not both.");
        }

        if (query.Limit is < 1 or > GetMessageHistoryQuery.MaxLimit)
        {
            throw new RequestValidationException($"'limit' must be between 1 and {GetMessageHistoryQuery.MaxLimit}.");
        }

        await db.EnsureMemberAsync(query.RoomId, query.UserName, cancellationToken);

        var inRoom = read.Messages.Where(m => m.RoomId == query.RoomId);
        var before = query.Before ?? long.MaxValue;
        var ordered = query.After is { } after
            ? inRoom.Where(m => m.Id > after).OrderBy(m => m.Id)
            : inRoom.Where(m => m.Id < before).OrderByDescending(m => m.Id);

        // One extra row tells us whether another page exists without a COUNT query.
        var rows = await ordered
            .Take(query.Limit + 1)
            .Select(m => new MessageDto(m.Id, m.RoomId, m.Sender, m.Body, m.SentAt, m.Revision, m.EditedAt, m.DeletedAt != null))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        var page = rows.Take(query.Limit).OrderBy(m => m.Id).ToList();
        return new MessagePage(page, hasMore);
    }
}
