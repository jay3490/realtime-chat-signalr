using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.EventLog;

/// <summary>
/// Publishes to Kafka with an idempotent producer: the broker discards retried duplicates
/// and keeps each partition in send order, and every event is acknowledged by all in-sync
/// replicas before it counts as published.
/// </summary>
public sealed partial class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(10);

    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaEventPublisher> _logger;
    private readonly IProducer<string, string> _producer;

    public KafkaEventPublisher(IOptions<KafkaOptions> options, ILogger<KafkaEventPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
        var config = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            ClientId = _options.ClientId,
            EnableIdempotence = true,
            Acks = Acks.All,
            LingerMs = 5,
            CompressionType = CompressionType.Lz4,
        };

        _producer = new ProducerBuilder<string, string>(config)
            .SetErrorHandler((_, error) => LogProducerError(_logger, error.Code, error.Reason))
            .Build();
    }

    public async Task<PublishResult> PublishAsync(IReadOnlyList<OutgoingEvent> batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);

        // Everything is handed to the producer before awaiting, so the batch goes out in a
        // few requests instead of one round trip per event. Order within a partition is kept.
        var deliveries = new List<Task<DeliveryResult<string, string>>>(batch.Count);
        foreach (var item in batch)
        {
            var message = new Message<string, string>
            {
                Key = item.Key,
                Value = item.Payload,
                Headers = new Headers
                {
                    { "event-id", Encoding.UTF8.GetBytes(item.EventId.ToString("D")) },
                    { "event-type", Encoding.UTF8.GetBytes(item.EventType) },
                },
            };
            deliveries.Add(_producer.ProduceAsync(_options.TopicFor(item.Stream), message, cancellationToken));
        }

        string? error = null;
        try
        {
            await Task.WhenAll(deliveries);
        }
        catch (KafkaException ex)
        {
            error = ex.Error.Reason;
            LogPublishFailed(_logger, ex, ex.Error.Code);
        }

        return new PublishResult(deliveries.TakeWhile(d => d.IsCompletedSuccessfully).Count(), error);
    }

    public void Dispose()
    {
        _producer.Flush(FlushTimeout);
        _producer.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kafka producer error {Code}: {Reason}")]
    private static partial void LogProducerError(ILogger logger, ErrorCode code, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing to Kafka failed with {Code}; the rest of the batch will be retried")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, ErrorCode code);
}
