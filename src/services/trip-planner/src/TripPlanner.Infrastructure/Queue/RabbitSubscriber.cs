using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TripPlanner.Application.RouteCalculator;

namespace TripPlanner.Infrastructure.Queue;

internal class RabbitSubscriber<T> : IQueueSubscriber<T>
{
    private readonly IConnectionFactory _factory;
    private IConnection? _connection;
    private IChannel? _channel;
    
    private readonly IMessageHandler<T> _handler;
    
    public RabbitSubscriber(IMessageHandler<T> handler, IConnectionFactory factory)
    {
        _handler = handler;
        _factory = factory;
    }
    
    public async Task StartAsync(CancellationToken ct)
    {
        _connection = await _factory.CreateConnectionAsync(ct);
        _channel = await _connection.CreateChannelAsync(null, ct);
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (model, ea) =>
        {
            var payload = JsonSerializer.Deserialize<T>(ea.Body.Span);
            try
            {
                if (payload == null)
                {
                    throw new NullReferenceException("Payload is null");
                }
                
                await _handler.HandleAsync(payload, ct);
                
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
        
        await _channel.BasicConsumeAsync(queue: "_queueName", autoAck: false, consumer: consumer, cancellationToken: ct);
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}