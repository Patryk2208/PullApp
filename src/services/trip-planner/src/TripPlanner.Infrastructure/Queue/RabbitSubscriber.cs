using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TripPlanner.Application.Services;

namespace TripPlanner.Infrastructure.Queue;

// RabbitSubscriber is singleton but IHandler<T> is scoped (it holds DbSession).
// We resolve a fresh scope per message so lifetime is respected.
public class RabbitSubscriber<T>(
    IServiceScopeFactory scopeFactory,
    IConnectionFactory factory,
    IQueueDomainMapper<T> mapper,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitSubscriber<T>> logger) : ISubscriber
{
    private IChannel? _channel;
    private readonly RabbitMqOptions _options = options.Value;

    public async Task StartAsync(CancellationToken ct)
    {
        var connection = await factory.CreateConnectionAsync(ct);
        _channel = await connection.CreateChannelAsync(new CreateChannelOptions(false, false), ct);
        var consumer = new AsyncEventingBasicConsumer(_channel);

        consumer.ReceivedAsync += async (_, ea) =>
        {
            T? domainModel;
            try
            {
                domainModel = mapper.ToDomain(ea.Body);
                if (domainModel is null)
                {
                    logger.LogWarning("Mapped payload is null — dead-lettering message");
                    await _channel.BasicNackAsync(ea.DeliveryTag, false, false, ct);
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to map RabbitMQ message body — dead-lettering");
                await _channel.BasicNackAsync(ea.DeliveryTag, false, false, ct);
                return;
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<IHandler<T>>();
                await handler.HandleAsync(domainModel, ct);
                await _channel.BasicAckAsync(ea.DeliveryTag, false, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Handler threw — requeueing message");
                await _channel.BasicNackAsync(ea.DeliveryTag, false, requeue: true, ct);
            }
        };

        await _channel.QueueDeclareAsync(_options.Results, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: (ushort)_options.PrefetchCount, global: false, ct);
        await _channel.BasicConsumeAsync(queue: _options.Results, autoAck: false, consumer, ct);

        logger.LogInformation("RabbitMQ subscriber started on queue {Queue} prefetch={Prefetch}",
            _options.Results, _options.PrefetchCount);
    }

    public Task StopAsync(CancellationToken ct)
    {
        _channel?.Dispose();
        return Task.CompletedTask;
    }
}