using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TripPlanner.Application.Services;

namespace TripPlanner.Infrastructure.Queue;

public class RabbitSubscriber<T>(IHandler<T> handler, IConnectionFactory factory, 
    IQueueDomainMapper<T> mapper, IOptions<RabbitMqOptions> options) : ISubscriber
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
            var domainModel = mapper.ToDomain(ea.Body);
            try
            {
                if (domainModel == null)
                {
                    throw new NullReferenceException("Payload is null");
                }
                
                await handler.HandleAsync(domainModel, ct);
                
                await _channel.BasicAckAsync(ea.DeliveryTag, false, ct);
            }
            catch (NullReferenceException)
            {
                await _channel.BasicNackAsync(ea.DeliveryTag, false, false, ct);
            }
            catch (Exception)
            {
                await _channel.BasicNackAsync(ea.DeliveryTag, false, true, ct);
            }
        };
        
        await _channel.QueueDeclareAsync(_options.Results, true, false, false, cancellationToken: ct);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: (ushort)_options.PrefetchCount, global: false, ct);
        await _channel.BasicConsumeAsync(queue: _options.Results, false, consumer, ct);
    }

    public Task StopAsync(CancellationToken ct)
    {
        _channel?.Dispose();
        return Task.CompletedTask;
    }
}