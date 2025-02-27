using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.EventLog;

/// <summary>
/// A Kafka consumer in a consumer group. Offsets are stored only after the caller has
/// handled an event (<see cref="Commit"/>), and the client commits stored offsets in the
/// background, so delivery is at-least-once.
/// </summary>
public sealed partial class KafkaEventConsumer : IEventLogConsumer
{
    private readonly KafkaOptions _options;
    private readonly ConsumerSettings _settings;
    private readonly ILogger _logger;
    private readonly IConsumer<string, string> _consumer;

    public KafkaEventConsumer(KafkaOptions options, ConsumerSettings settings, ILogger logger)
    {
        _options = options;
        _settings = settings;
        _logger = logger;

        var config = new ConsumerConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = options.ClientId,
            GroupId = settings.GroupId,
            EnableAutoCommit = true,
            EnableAutoOffsetStore = false,
            AutoOffsetReset = settings.ResumeAt is null ? AutoOffsetReset.Latest : AutoOffsetReset.Earliest,
        };

        _consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, error) => LogConsumerError(_logger, error.Code, error.Reason))
            .SetPartitionsAssignedHandler((_, partitions) => partitions.Select(StartPosition))
            .Build();
    }

    public void Subscribe(IReadOnlyCollection<string> streams) =>
        _consumer.Subscribe(streams.Select(_options.TopicFor));

    public ConsumedEvent? Consume(TimeSpan timeout)
    {
        var result = _consumer.Consume(timeout);
        if (result?.Message is null)
        {
            return null;
        }

        return ConsumedEvent.Parse(
            _options.StreamOf(result.Topic), result.Partition.Value, result.Offset.Value, result.Message.Value);
    }

    public void Commit(ConsumedEvent consumed)
    {
        ArgumentNullException.ThrowIfNull(consumed);
        try
        {
            _consumer.StoreOffset(new TopicPartitionOffset(
                _options.TopicFor(consumed.Stream), new Partition(consumed.Partition), new Offset(consumed.Offset + 1)));
        }
        catch (KafkaException ex)
        {
            // The partition was revoked in a rebalance; its new owner resumes from its own position.
            LogStoreFailed(_logger, ex, consumed.Stream, consumed.Partition);
        }
    }

    public void Close() => _consumer.Close();

    public void Dispose() => _consumer.Dispose();

    private TopicPartitionOffset StartPosition(TopicPartition partition)
    {
        if (_settings.ResumeAt is null)
        {
            return new TopicPartitionOffset(partition, Offset.Stored);
        }

        var next = _settings.ResumeAt(_options.StreamOf(partition.Topic), partition.Partition.Value);
        return new TopicPartitionOffset(partition, next is { } offset ? new Offset(offset) : Offset.Beginning);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kafka consumer error {Code}: {Reason}")]
    private static partial void LogConsumerError(ILogger logger, ErrorCode code, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Could not store the offset for {Stream}/{Partition}")]
    private static partial void LogStoreFailed(ILogger logger, Exception exception, string stream, int partition);
}

public sealed class KafkaEventConsumerFactory(IOptions<KafkaOptions> options, ILoggerFactory loggers) : IEventLogConsumerFactory
{
    public IEventLogConsumer Create(ConsumerSettings settings) =>
        new KafkaEventConsumer(options.Value, settings, loggers.CreateLogger<KafkaEventConsumer>());
}
