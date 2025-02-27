using Chat.Contracts;
using Chat.Infrastructure.EventLog;
using Chat.Infrastructure.Persistence;
using Chat.Infrastructure.ReadModel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Chat.UnitTests.Support;

/// <summary>
/// A read model database in SQLite memory plus a fake log: <see cref="ProjectAsync"/> gives
/// each event the next offset of its stream, as a single-partition topic would.
/// </summary>
public sealed class SqliteReadModel : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Dictionary<string, long> _nextOffset = new(StringComparer.Ordinal);
    private long _lastOutboxId;

    private SqliteReadModel(SqliteConnection connection) => _connection = connection;

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));

    /// <summary>Everything appended so far, in order, so tests can replay it.</summary>
    public List<ConsumedEvent> Log { get; } = [];

    public static async Task<SqliteReadModel> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var readModel = new SqliteReadModel(connection);

        await using var db = readModel.NewContext();
        await db.Database.EnsureCreatedAsync();
        return readModel;
    }

    public ReadModelDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ReadModelDbContext>().UseSqlite(_connection).Options);

    /// <summary>A context for the query side, as the API would resolve it.</summary>
    public ChatReadDbContext NewQueryContext() =>
        new(new DbContextOptionsBuilder<ChatReadDbContext>().UseSqlite(_connection).Options);

    /// <summary>
    /// Projects every outbox row of the write database not seen yet, the way the relay and
    /// the projection would, so query handlers can be tested against commands' real effects.
    /// </summary>
    public async Task CatchUpAsync(SqliteChatDatabase write)
    {
        ArgumentNullException.ThrowIfNull(write);
        await using var db = write.NewContext();
        var pending = await db.OutboxMessages
            .Where(m => m.Id > _lastOutboxId)
            .OrderBy(m => m.Id)
            .ToListAsync();
        foreach (var row in pending)
        {
            await ProjectAsync(Append(ChatEventSerializer.Deserialize(row.Payload)));
            _lastOutboxId = row.Id;
        }
    }

    public ReadModelProjector NewProjector(ReadModelDbContext db) =>
        new(db, Clock, NullLogger<ReadModelProjector>.Instance);

    /// <summary>Appends the events to the fake log and projects them.</summary>
    public async Task<List<ProjectionOutcome>> ProjectAsync(params IChatEvent[] events)
    {
        var outcomes = new List<ProjectionOutcome>();
        foreach (var chatEvent in events)
        {
            outcomes.Add(await ProjectAsync(Append(EventEnvelope.Wrap(chatEvent, Guid.NewGuid()))));
        }

        return outcomes;
    }

    public ConsumedEvent Append(EventEnvelope envelope)
    {
        var offset = _nextOffset.GetValueOrDefault(envelope.Stream);
        _nextOffset[envelope.Stream] = offset + 1;
        var consumed = new ConsumedEvent(envelope.Stream, 0, offset, envelope);
        Log.Add(consumed);
        return consumed;
    }

    public async Task<ProjectionOutcome> ProjectAsync(ConsumedEvent consumed)
    {
        await using var db = NewContext();
        return await NewProjector(db).ProjectAsync(consumed, default);
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
