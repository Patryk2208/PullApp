using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TripPlanner.Application.Services;

namespace TripPlanner.Infrastructure.Queue;

public class RabbitComputePublisher<T>(RabbitConnection connectionProvider, IQueueDtoMapper<T> mapper, IOptions<RabbitMqOptions> options)
    : IComputePublisher<T>
{
    private readonly RabbitMqOptions _options = options.Value;
 
    public async Task PublishAsync(T payload, CancellationToken ct)
    {
        var connection = await connectionProvider.GetAsync(ct);
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true), ct);
        await channel.QueueDeclareAsync(_options.Compute, true, false, false, cancellationToken: ct);

        var bytes = mapper.ToDto(payload);
        await channel.BasicPublishAsync(exchange: "", routingKey: _options.Compute, true, bytes, ct);
    }
}
