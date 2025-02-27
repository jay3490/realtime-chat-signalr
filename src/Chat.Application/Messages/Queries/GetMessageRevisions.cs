using Chat.Application.Abstractions;
using Chat.Application.Common;
using Chat.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Messages.Queries;

public sealed record MessageRevisionDto(int Revision, string Body, DateTimeOffset RecordedAt);

/// <summary>
/// Every version of a message's text, oldest first, as rebuilt from its MessageSent and
/// MessageEdited events. Empty for a deleted message.
/// </summary>
public sealed record GetMessageRevisionsQuery(Guid RoomId, long MessageId, string UserName)
    : IQuery<IReadOnlyList<MessageRevisionDto>>;

internal sealed class GetMessageRevisionsHandler(IChatDbContext db, IChatReadDbContext read)
    : IQueryHandler<GetMessageRevisionsQuery, IReadOnlyList<MessageRevisionDto>>
{
    public async Task<IReadOnlyList<MessageRevisionDto>> HandleAsync(
        GetMessageRevisionsQuery query, CancellationToken cancellationToken)
    {
        await db.EnsureMemberAsync(query.RoomId, query.UserName, cancellationToken);

        if (!await read.Messages.AnyAsync(m => m.Id == query.MessageId && m.RoomId == query.RoomId, cancellationToken))
        {
            throw new NotFoundException("Message not found in this room.");
        }

        return await read.MessageRevisions
            .Where(r => r.MessageId == query.MessageId)
            .OrderBy(r => r.Revision)
            .Select(r => new MessageRevisionDto(r.Revision, r.Body, r.RecordedAt))
            .ToListAsync(cancellationToken);
    }
}
