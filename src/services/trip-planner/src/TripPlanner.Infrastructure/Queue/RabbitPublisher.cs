using RabbitMQ.Client;
using TripPlanner.Application.RouteCalculator;
using TripPlanner.Domain;

namespace TripPlanner.Infrastructure.Queue;

internal class RabbitPublisher<T> : IQueuePublisher<T>
{
    private readonly IConnectionFactory _factory;
    
    public RabbitPublisher(IConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task PublishAsync(T payload, CancellationToken ct)
    {
        var connection = await _factory.CreateConnectionAsync(ct);
        var channel = await connection.CreateChannelAsync(null, ct);
        await channel.QueueDeclareAsync(queue: "queueName", durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        
        var msg = new ReadOnlyMemory<byte>();
        //todo map to msg from payload
        
        await channel.BasicPublishAsync(exchange: "", routingKey: "queue_name", mandatory: true, msg, ct);
    }
}