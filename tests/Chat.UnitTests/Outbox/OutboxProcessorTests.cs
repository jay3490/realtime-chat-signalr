using Chat.Contracts;
using Chat.Infrastructure.EventLog;
using Chat.Infrastructure.Outbox;
using Chat.UnitTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Chat.UnitTests.Outbox;

public sealed class OutboxProcessorTests : IAsyncLifetime
{
    private readonly FakePublisher _publisher = new();
    private SqliteChatDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _database = await SqliteChatDatabase.CreateAsync();

        // Five UserRegistered rows, ids 1-5.
        await _database.RegisterUsersAsync("u1", "u2", "u3", "u4", "u5");
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Pending_rows_are_published_in_order_and_marked()
    {
        var result = await ProcessAsync();

        Assert.Equal(new OutboxBatchResult(5, 5), result);
        Assert.Equal(["u1", "u2", "u3", "u4", "u5"], _publisher.Sent.Select(e => e.Key));
        Assert.All(_publisher.Sent, e => Assert.Equal(EventStreams.Users, e.Stream));
        Assert.Empty(await UnpublishedAsync());

        Assert.Equal(new OutboxBatchResult(0, 0), await ProcessAsync());
        Assert.Equal(5, _publisher.Sent.Count);
    }

    [Fact]
    public async Task Only_the_acknowledged_prefix_is_marked_and_the_rest_is_sent_again()
    {
        _publisher.AcknowledgeAtMost = 2;

        var result = await ProcessAsync();

        Assert.True(result.Failed);
        Assert.Equal(["u3", "u4", "u5"], (await UnpublishedAsync()).Select(m => m.StreamKey));
        var stuck = (await UnpublishedAsync())[0];
        Assert.Equal((1, "broker unavailable"), (stuck.Attempts, stuck.LastError));

        _publisher.AcknowledgeAtMost = int.MaxValue;
        _publisher.Sent.Clear();
        await ProcessAsync();

        Assert.Equal(["u3", "u4", "u5"], _publisher.Sent.Select(e => e.Key));
        Assert.Empty(await UnpublishedAsync());
    }

    [Fact]
    public async Task A_publisher_that_throws_leaves_every_row_pending()
    {
        _publisher.Throw = true;

        var result = await ProcessAsync();

        Assert.Equal(new OutboxBatchResult(5, 0), result);
        Assert.Equal(5, (await UnpublishedAsync()).Count);
        Assert.Equal("connection refused", (await UnpublishedAsync())[0].LastError);
    }

    [Fact]
    public async Task Batches_are_limited_to_the_configured_size()
    {
        var result = await ProcessAsync(batchSize: 2);

        Assert.Equal(new OutboxBatchResult(2, 2), result);
        Assert.Equal(["u1", "u2"], _publisher.Sent.Select(e => e.Key));
    }

    [Fact]
    public async Task Published_rows_are_deleted_once_past_retention()
    {
        await ProcessAsync(batchSize: 3);
        _database.Clock.Advance(TimeSpan.FromHours(73));

        await using var db = _database.NewContext();
        var processor = NewProcessor(db, batchSize: 100);
        var deleted = await processor.DeletePublishedAsync(default);

        Assert.Equal(3, deleted);
        Assert.Equal(2, await db.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Payloads_reach_the_publisher_unchanged()
    {
        await ProcessAsync();

        var envelope = ChatEventSerializer.Deserialize(_publisher.Sent[0].Payload);

        Assert.Equal(("UserRegistered", "u1"), (envelope.EventType, ((UserRegistered)envelope.Event).UserName));
        Assert.Equal(envelope.EventId, _publisher.Sent[0].EventId);
    }

    private async Task<OutboxBatchResult> ProcessAsync(int batchSize = 100)
    {
        await using var db = _database.NewContext();
        return await NewProcessor(db, batchSize).PublishPendingAsync(default);
    }

    private OutboxProcessor NewProcessor(Infrastructure.Persistence.ChatDbContext db, int batchSize) =>
        new(db, _publisher, _database.Clock,
            Options.Create(new OutboxOptions { BatchSize = batchSize, RetentionHours = 72 }),
            NullLogger<OutboxProcessor>.Instance);

    private async Task<List<OutboxMessage>> UnpublishedAsync()
    {
        await using var db = _database.NewContext();
        return await db.OutboxMessages.AsNoTracking().Where(m => m.PublishedAt == null).OrderBy(m => m.Id).ToListAsync();
    }

    private sealed class FakePublisher : IEventPublisher
    {
        public List<OutgoingEvent> Sent { get; } = [];

        public int AcknowledgeAtMost { get; set; } = int.MaxValue;

        public bool Throw { get; set; }

        public Task<PublishResult> PublishAsync(IReadOnlyList<OutgoingEvent> batch, CancellationToken cancellationToken)
        {
            if (Throw)
            {
                throw new InvalidOperationException("connection refused");
            }

            var acknowledged = Math.Min(AcknowledgeAtMost, batch.Count);
            Sent.AddRange(batch.Take(acknowledged));
            return Task.FromResult(acknowledged < batch.Count
                ? new PublishResult(acknowledged, "broker unavailable")
                : new PublishResult(acknowledged));
        }
    }
}
