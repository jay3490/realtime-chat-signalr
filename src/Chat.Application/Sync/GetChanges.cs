using Chat.Application.Abstractions;
using Chat.Application.Common;
using Chat.Application.Messages;
using Chat.Application.Persistence;
using Chat.Application.ReadModel;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Sync;

/// <param name="Kind">One of <see cref="SyncChangeKinds"/>.</param>
/// <param name="Message">The message's current state, for message changes.</param>
/// <param name="MessageId">The message changed, or for a read receipt the message read up to.</param>
/// <param name="UserName">Who joined, who read, or who created the room.</param>
public sealed record SyncChangeDto(
    Guid RoomId,
    string Kind,
    MessageDto? Message,
    long? MessageId,
    string? UserName,
    DateTimeOffset OccurredAt);

/// <param name="Cursor">Pass this back to get the changes after this page.</param>
/// <param name="HasMore">More changes are waiting; ask again straight away.</param>
/// <param name="Reset">
/// The cursor belongs to a read model that has since been rebuilt (or is otherwise unknown):
/// the device must reload its state and continue from <paramref name="Cursor"/>.
/// </param>
public sealed record SyncPage(IReadOnlyList<SyncChangeDto> Changes, string Cursor, bool HasMore, bool Reset);

/// <summary>
/// Changes in the user's rooms since <paramref name="Cursor"/>, in the order the read model
/// applied them. Without a cursor it returns the current head and no changes: a new device
/// takes the head first, then loads its state, then syncs from there, so nothing falls in
/// between. Every device keeps its own cursor, so all of them converge on the same state.
/// </summary>
public sealed record GetChangesQuery(string UserName, string? Cursor, int Limit = GetChangesQuery.DefaultLimit) : IQuery<SyncPage>
{
    public const int DefaultLimit = 200;
    public const int MaxLimit = 500;
}

internal sealed class GetChangesHandler(IChatReadDbContext read) : IQueryHandler<GetChangesQuery, SyncPage>
{
    public async Task<SyncPage> HandleAsync(GetChangesQuery query, CancellationToken cancellationToken)
    {
        if (query.Limit is < 1 or > GetChangesQuery.MaxLimit)
        {
            throw new RequestValidationException($"'limit' must be between 1 and {GetChangesQuery.MaxLimit}.");
        }

        var epoch = await read.State.Select(s => s.Epoch).SingleAsync(cancellationToken);
        var head = new SyncCursor(epoch, await read.SyncEntries.MaxAsync(e => (long?)e.Position, cancellationToken) ?? 0);

        if (query.Cursor is null)
        {
            return new SyncPage([], head.ToString(), HasMore: false, Reset: false);
        }

        if (!SyncCursor.TryParse(query.Cursor, out var cursor))
        {
            throw new RequestValidationException("The sync cursor is malformed.");
        }

        if (cursor.Value.Epoch != epoch || cursor.Value.Position > head.Position)
        {
            return new SyncPage([], head.ToString(), HasMore: false, Reset: true);
        }

        var after = cursor.Value.Position;
        var entries = await read.SyncEntries
            .Where(e => e.Position > after
                && read.Members.Any(m => m.RoomId == e.RoomId && m.UserName == query.UserName))
            .OrderBy(e => e.Position)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = entries.Count > query.Limit;
        var page = entries.Take(query.Limit).ToList();

        var messageIds = page
            .Where(e => e.Kind != SyncChangeKinds.ReadReceipt && e.MessageId is not null)
            .Select(e => e.MessageId!.Value)
            .Distinct()
            .ToList();
        var messages = await read.Messages
            .Where(m => messageIds.Contains(m.Id))
            .Select(m => new MessageDto(m.Id, m.RoomId, m.Sender, m.Body, m.SentAt, m.Revision, m.EditedAt, m.DeletedAt != null))
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        var changes = page
            .Select(e => new SyncChangeDto(
                e.RoomId,
                e.Kind,
                e.Kind != SyncChangeKinds.ReadReceipt && e.MessageId is { } id ? messages.GetValueOrDefault(id) : null,
                e.MessageId,
                e.UserName,
                e.OccurredAt))
            .ToList();

        // With nothing visible to this user the cursor still moves to the head, so the next
        // request does not scan the same entries again.
        var next = page.Count > 0 ? new SyncCursor(epoch, page[^1].Position) : head;
        return new SyncPage(changes, next.ToString(), hasMore, Reset: false);
    }
}
