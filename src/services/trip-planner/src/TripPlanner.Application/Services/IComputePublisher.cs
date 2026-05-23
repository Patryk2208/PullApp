namespace TripPlanner.Application.Services;

public interface IComputePublisher<in T>
{
    Task PublishAsync(T payload, CancellationToken ct);
}