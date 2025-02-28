using Chat.Contracts;
using Chat.Infrastructure.EventLog;
using Chat.Infrastructure.ReadModel;
using Chat.UnitTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chat.UnitTests.Projections;

public sealed class ProjectionWorkerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset At = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryEventLog _log = new();
    private SqliteReadModel _readModel = null!;

    public async Task InitializeAsync() => _readModel = await SqliteReadModel.CreateAsync();

    public async Task DisposeAsync() => await _readModel.DisposeAsync();

    [Fact]
    public async Task The_worker_projects_the_log_and_resumes_from_its_checkpoint_after_a_restart()
    {
        Append(new UserRegistered("alice", At));
        Append(new UserRegistered("bob", At));

        await RunUntilAsync(async db => await db.Users.CountAsync() == 2);

        // Offsets 0 and 1 are checkpointed; a restarted worker must only apply what is new.
        Append(new UserRegistered("carol", At));
        await RunUntilAsync(async db => await db.Users.CountAsync() == 3);

        await using var check = _readModel.NewContext();
        Assert.Equal(3, await check.ProcessedEvents.CountAsync());
        Assert.Equal(2, (await check.Checkpoints.SingleAsync(c => c.Stream == EventStreams.Users)).Offset);
    }

    [Fact]
    public async Task A_rebuild_replays_the_whole_log()
    {
        Append(new UserRegistered("alice", At));
        await RunUntilAsync(async db => await db.Users.CountAsync() == 1);

        await using (var db = _readModel.NewContext())
        {
            await new ReadModelRebuilder(db, NullLogger<ReadModelRebuilder>.Instance).ResetAsync(default);
            Assert.Equal(0, await db.Users.CountAsync());
        }

        await RunUntilAsync(async db => await db.Users.CountAsync() == 1);
    }

    private void Append(IChatEvent chatEvent)
    {
        var envelope = EventEnvelope.Wrap(chatEvent, Guid.NewGuid());
        _log.Append(envelope.Stream, envelope.StreamKey, ChatEventSerializer.Serialize(envelope));
    }

    private async Task RunUntilAsync(Func<ReadModelDbContext, Task<bool>> done)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(TimeProvider.System)
            .AddSingleton<IEventLogConsumerFactory>(new InMemoryEventConsumerFactory(_log))
            .AddScoped(_ => _readModel.NewContext())
            .AddScoped<ReadModelProjector>()
            .AddSingleton<ProjectionWorker>();
        await using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<ProjectionWorker>();

        await worker.StartAsync(default);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                await using var db = _readModel.NewContext();
                if (await done(db))
                {
                    return;
                }

                await Task.Delay(20, timeout.Token);
            }
        }
        finally
        {
            await worker.StopAsync(default);
        }
    }
}
