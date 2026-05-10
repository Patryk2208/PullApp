using RabbitMQ.Client;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;

namespace TripPlanner.Infrastructure.Queue;

internal class RabbitComputePublisher<T>(IConnectionFactory factory, IQueueDtoMapper<T> mapper, RabbitMqOptions options)
    : IComputePublisher<T>
{
    public async Task PublishAsync(T payload, CancellationToken ct)
    {
        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(null, ct);
        await channel.QueueDeclareAsync(options.ComputeQueueName, true, false, false, cancellationToken: ct);

        var bytes = mapper.ToDto(payload);
        await channel.BasicPublishAsync(exchange: "", routingKey: options.ComputeQueueName, true, bytes, ct);
    }
}
