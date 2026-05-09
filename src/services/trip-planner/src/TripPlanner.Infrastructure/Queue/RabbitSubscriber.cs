using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TripPlanner.Application.Repositories;

namespace TripPlanner.Infrastructure.Queue;

internal class RabbitSubscriber<T>(IHandler<T> handler, IConnectionFactory factory, 
    IQueueDomainMapper<T> mapper, RabbitMqOptions options) : ISubscriber
{
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task StartAsync(CancellationToken ct)
    {
        _connection = await factory.CreateConnectionAsync(ct);
        _channel = await _connection.CreateChannelAsync(null, ct);
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (model, ea) =>
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
        
        await _channel.BasicConsumeAsync(queue: options.ResultQueueName, false, consumer, ct);
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}