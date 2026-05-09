using System.Text.Json;
using Confluent.Kafka;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Events;

namespace TripPlanner.Infrastructure.Kafka;

public class KafkaPublisher : IKafkaPublisher, IDisposable
{
    private readonly IProducer<Null, string> _producer;

    public KafkaPublisher(KafkaOptions options)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            Acks             = Acks.Leader,
        };

        if (!string.IsNullOrEmpty(options.SaslUsername))
        {
            config.SecurityProtocol = SecurityProtocol.SaslPlaintext;
            config.SaslMechanism    = SaslMechanism.Plain;
            config.SaslUsername     = options.SaslUsername;
            config.SaslPassword     = options.SaslPassword;
        }

        _producer = new ProducerBuilder<Null, string>(config).Build();
    }

    public async Task PublishAsync<T>(string topic, T payload, CancellationToken ct)
        where T : IEvent
    {
        var envelope = new Envelope<T>(
            Guid.NewGuid(),
            payload.EventType,
            DateTimeOffset.UtcNow,
            payload);

        var json = JsonSerializer.Serialize(envelope);

        await _producer.ProduceAsync(topic, new Message<Null, string> { Value = json }, ct);
    }

    public void Dispose() => _producer.Dispose();
}
