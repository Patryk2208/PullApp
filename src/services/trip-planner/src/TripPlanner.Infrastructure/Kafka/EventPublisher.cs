using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Events;

namespace TripPlanner.Infrastructure.Kafka;

public class EventPublisher : IEventPublisher, IDisposable
{
    private readonly IProducer<Null, string> _producer;

    public EventPublisher(IOptions<KafkaOptions> options)
    {
        var kafkaOptions = options.Value;
        
        var config = new ProducerConfig
        {
            BootstrapServers = kafkaOptions.BootstrapServers,
            Acks             = Acks.Leader,
        };

        if (!string.IsNullOrEmpty(kafkaOptions.SaslUsername))
        {
            config.SecurityProtocol = SecurityProtocol.SaslPlaintext;
            config.SaslMechanism    = SaslMechanism.Plain;
            config.SaslUsername     = kafkaOptions.SaslUsername;
            config.SaslPassword     = kafkaOptions.SaslPassword;
        }

        _producer = new ProducerBuilder<Null, string>(config).Build();
    }

    public async Task PublishAsync<T>(string topic, T payload, CancellationToken ct)
        where T : IDomainEvent
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
