using Chat.Contracts;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.EventLog;

public static class KafkaTopics
{
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Creates the event topics if they do not exist. The topics are the system of record
    /// for the read models, so they keep everything (<c>retention.ms=-1</c>) and are never
    /// compacted: a projection can only be rebuilt from a complete history.
    /// </summary>
    public static async Task EnsureEventTopicsAsync(this IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<KafkaOptions>>().Value;
        if (!options.IsConfigured)
        {
            return;
        }

        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = options.BootstrapServers }).Build();
        var existing = admin.GetMetadata(MetadataTimeout).Topics
            .Where(t => !t.Error.IsError)
            .Select(t => t.Topic)
            .ToHashSet(StringComparer.Ordinal);

        var missing = EventStreams.All
            .Select(options.TopicFor)
            .Where(topic => !existing.Contains(topic))
            .Select(topic => new TopicSpecification
            {
                Name = topic,
                NumPartitions = options.Partitions,
                ReplicationFactor = options.ReplicationFactor,
                Configs = new Dictionary<string, string>
                {
                    ["retention.ms"] = "-1",
                    ["retention.bytes"] = "-1",
                    ["cleanup.policy"] = "delete",
                },
            })
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        try
        {
            await admin.CreateTopicsAsync(missing);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code is ErrorCode.NoError or ErrorCode.TopicAlreadyExists))
        {
            // Another instance created them first.
        }
    }
}
