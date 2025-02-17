using System.Data;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Chat.Infrastructure.Outbox;

/// <summary>
/// Decides which instance relays the outbox. Two relays reading the same rows could each
/// send a different slice and break per-room order, so only the lease holder publishes.
/// </summary>
public interface IOutboxLease
{
    /// <summary>True while this instance holds the lease; acquires it if it is free.</summary>
    Task<bool> TryAcquireAsync(CancellationToken cancellationToken);
}

/// <summary>Always held. Correct only when a single process writes to the database (SQLite).</summary>
public sealed class LocalOutboxLease : IOutboxLease
{
    public Task<bool> TryAcquireAsync(CancellationToken cancellationToken) => Task.FromResult(true);
}

/// <summary>
/// A PostgreSQL session-level advisory lock held on a dedicated connection. If this process
/// dies or loses the connection, the server drops the lock and another replica takes over.
/// </summary>
public sealed partial class PostgresOutboxLease : IOutboxLease, IAsyncDisposable
{
    // Arbitrary but fixed: the bytes of "chat-out".
    private const long LockKey = 0x63686174_2d6f7574;

    private readonly string _connectionString;
    private readonly ILogger<PostgresOutboxLease> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private NpgsqlConnection? _connection;

    public PostgresOutboxLease(string connectionString, ILogger<PostgresOutboxLease> logger)
    {
        // A pooled connection could return to the pool still holding the lock.
        _connectionString = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
        _logger = logger;
    }

    public async Task<bool> TryAcquireAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { State: ConnectionState.Open } held && await IsAliveAsync(held, cancellationToken))
            {
                return true;
            }

            await DropConnectionAsync();

            var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", LockKey);
            if (await command.ExecuteScalarAsync(cancellationToken) is true)
            {
                _connection = connection;
                LogAcquired(_logger);
                return true;
            }

            await connection.DisposeAsync();
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DropConnectionAsync();
        _gate.Dispose();
    }

    private async Task<bool> IsAliveAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var ping = new NpgsqlCommand("SELECT 1", connection);
            await ping.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (NpgsqlException ex)
        {
            LogLost(_logger, ex);
            return false;
        }
    }

    private async Task DropConnectionAsync()
    {
        if (_connection is null)
        {
            return;
        }

        // Closing the session releases the advisory lock.
        await _connection.DisposeAsync();
        _connection = null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "This instance now relays the outbox")]
    private static partial void LogAcquired(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lost the outbox lease connection")]
    private static partial void LogLost(ILogger logger, Exception exception);
}
