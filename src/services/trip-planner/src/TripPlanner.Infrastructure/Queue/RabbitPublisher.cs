using RabbitMQ.Client;
using TripPlanner.Application.RouteCalculator;
using TripPlanner.Domain;

namespace TripPlanner.Infrastructure.Queue;

internal class RabbitPublisher<T>(IConnectionFactory factory, IQueueDtoMapper<T> mapper, RabbitMqOptions options) 
    : IQueuePublisher<T>
{
    public async Task PublishAsync(T payload, CancellationToken ct)
    {
        var connection = await factory.CreateConnectionAsync(ct);
        var channel = await connection.CreateChannelAsync(null, ct);
        await channel.QueueDeclareAsync(options.ComputeQueueName, true, false, false, cancellationToken: ct);

        var msg = mapper.ToDto(payload);
        
        await channel.BasicPublishAsync(exchange: "", routingKey: options.ComputeQueueName, true, msg, ct);
    }
}