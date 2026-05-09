namespace TripPlanner.Application.Repositories;

public interface IQueuePublisher<in T>
{
    Task PublishAsync(T payload, CancellationToken ct);
}
