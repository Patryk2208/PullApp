using TripPlanner.Application.Services;
using TripPlanner.Domain.Events;

namespace TripPlanner.Infrastructure.Fakes;

public class FakeKafkaPublisher : IKafkaPublisher
{
    public Task PublishAsync<T>(string topic, T payload, CancellationToken ct) where T : IEvent
        => Task.CompletedTask;
}
