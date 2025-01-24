using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chat.IntegrationTests;

/// <summary>Buffers one kind of server-to-client call so tests can await the next one.</summary>
public sealed class HubEvents<T>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly Channel<T> _received = Channel.CreateUnbounded<T>();

    public HubEvents(HubConnection connection, string method) =>
        connection.On<T>(method, item => _received.Writer.TryWrite(item));

    public async Task<T> NextAsync()
    {
        using var cts = new CancellationTokenSource(Timeout);
        return await _received.Reader.ReadAsync(cts.Token);
    }

    /// <summary>Waits for the next event that matches, skipping others (e.g. presence of unrelated users).</summary>
    public async Task<T> NextAsync(Func<T, bool> predicate)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (true)
        {
            var item = await _received.Reader.ReadAsync(cts.Token);
            if (predicate(item))
            {
                return item;
            }
        }
    }

    /// <summary>True if nothing arrives within <paramref name="window"/>.</summary>
    public async Task<bool> NoneWithinAsync(TimeSpan window)
    {
        using var cts = new CancellationTokenSource(window);
        try
        {
            await _received.Reader.WaitToReadAsync(cts.Token);
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }
}
