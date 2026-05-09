namespace TripPlanner.Application.Repositories;

public interface IPublisher<in T>
{
    Task PublishAsync(T payload, CancellationToken ct);
}