using Chat.Application.Abstractions;
using Chat.Domain.Common;
using Chat.Domain.Messages;
using Chat.Domain.Users;
using Chat.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Chat.UnitTests.Support;

/// <summary>
/// A real relational database (SQLite in memory) so that unique indexes, identity columns
/// and filtered includes behave the way they do in PostgreSQL, unlike the InMemory provider.
/// Each test gets its own database; the connection keeps it alive until disposal.
/// </summary>
public sealed class SqliteChatDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private SqliteChatDatabase(SqliteConnection connection) => _connection = connection;

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));

    public RecordingEventDispatcher Events { get; } = new();

    public static async Task<SqliteChatDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var database = new SqliteChatDatabase(connection);

        await using var db = database.NewContext();
        await db.Database.EnsureCreatedAsync();
        return database;
    }

    /// <summary>
    /// A fresh context, like a new request scope. Use one to arrange, another to act and
    /// a third to assert, so nothing is served from the change tracker by accident.
    /// </summary>
    public ChatDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ChatDbContext>().UseSqlite(_connection).Options, Events);

    public async Task RegisterUsersAsync(params string[] names)
    {
        await using var db = NewContext();
        foreach (var name in names)
        {
            db.Users.Add(User.Register(name, Clock.GetUtcNow()));
        }

        await db.SaveChangesAsync();
        Events.Clear();
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}

public sealed class RecordingEventDispatcher : IDomainEventDispatcher
{
    private readonly List<IDomainEvent> _events = [];

    /// <summary>Message ids at the moment of dispatch, to prove events fire after the insert.</summary>
    public List<long> MessageIdsAtDispatch { get; } = [];

    public IReadOnlyList<IDomainEvent> Dispatched => _events;

    public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            if (domainEvent is MessagePosted posted)
            {
                MessageIdsAtDispatch.Add(posted.Message.Id);
            }

            _events.Add(domainEvent);
        }

        return Task.CompletedTask;
    }

    public void Clear()
    {
        _events.Clear();
        MessageIdsAtDispatch.Clear();
    }
}
