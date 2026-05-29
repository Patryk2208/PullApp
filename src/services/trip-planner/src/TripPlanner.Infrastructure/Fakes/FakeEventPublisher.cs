using TripPlanner.Application.Services;
using TripPlanner.Domain.Events;

namespace TripPlanner.Infrastructure.Fakes;

public class FakeEventPublisher : IEventPublisher
{
    public Task PublishAsync<T>(string topic, T payload, CancellationToken ct) where T : IDomainEvent
        => Task.CompletedTask;
}
