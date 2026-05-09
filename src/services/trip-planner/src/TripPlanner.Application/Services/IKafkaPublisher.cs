using TripPlanner.Domain.Events;

namespace TripPlanner.Application.Services;

public interface IKafkaPublisher
{
    Task PublishAsync<T>(string topic, T payload, CancellationToken ct) where T : IEvent;
}
