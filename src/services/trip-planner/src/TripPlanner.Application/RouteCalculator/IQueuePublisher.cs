using TripPlanner.Domain;

namespace TripPlanner.Application.RouteCalculator;

public interface IQueuePublisher<in T>
{
    Task PublishAsync(T payload, CancellationToken ct);
}