using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;

namespace TripPlanner.Infrastructure.Kafka;

// Consumes inbound Kafka events from other services (currently DriverTracker).
// Each event type is dispatched to a handler via the IHandler.
public class KafkaConsumerService<T>(
    KafkaOptions options,
    IHandler<T> dispatcher,
    ILogger<KafkaConsumerService<T>> logger) : ISubscriber
{
    public async Task StartAsync(CancellationToken ct)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = options.BootstrapServers,
            GroupId          = options.ConsumerGroupId,
            AutoOffsetReset  = Enum.Parse<AutoOffsetReset>(options.AutoOffsetReset, ignoreCase: true),
            EnableAutoCommit = false,
        };

        if (!string.IsNullOrEmpty(options.SaslUsername))
        {
            config.SecurityProtocol = SecurityProtocol.SaslPlaintext;
            config.SaslMechanism    = SaslMechanism.Plain;
            config.SaslUsername     = options.SaslUsername;
            config.SaslPassword     = options.SaslPassword;
        }

        using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();
        consumer.Subscribe(options.DriverEventsTopic);

        logger.LogInformation("Kafka consumer started on topic {Topic}", options.DriverEventsTopic);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(ct);
                if (result?.Message?.Value is null) continue;

                T value;
                try
                {
                    var t = JsonSerializer.Deserialize<T>(result.Message.Value);
                    if (t is null)
                    {
                        logger.LogWarning("Kafka consumer returned null value");
                        continue;
                    }
                    value = t!;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error consuming Kafka message");
                    continue;
                }
                await dispatcher.HandleAsync(value, ct);
                consumer.Commit(result);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error consuming Kafka message");
            }
        }
        
        consumer.Close();
    }
}

// Routes inbound Kafka events by event_type to the right handler.
public class KafkaEventDispatcher(ILogger<KafkaEventDispatcher> logger) : IHandler<string>
{
    public async Task HandleAsync(string rawJson, CancellationToken ct)
    {
        logger.LogTrace($"Handling Kafka event {rawJson}");
    }
}
