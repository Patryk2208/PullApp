using RabbitMQ.Client;
using TripPlanner.Application.RouteCalculator;
using TripPlanner.Domain;

namespace TripPlanner.Infrastructure.Queue;

internal class RabbitPublisher<T>(IConnectionFactory factory, IQueueDtoMapper<T> mapper) : IQueuePublisher<T>
{
    public async Task PublishAsync(T payload, CancellationToken ct)
    {
        var connection = await factory.CreateConnectionAsync(ct);
        var channel = await connection.CreateChannelAsync(null, ct);
        await channel.QueueDeclareAsync(queue: "queueName", durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);

        var msg = mapper.ToDto(payload);
        
        await channel.BasicPublishAsync(exchange: "", routingKey: "queue_name", mandatory: true, msg, ct);
    }
}