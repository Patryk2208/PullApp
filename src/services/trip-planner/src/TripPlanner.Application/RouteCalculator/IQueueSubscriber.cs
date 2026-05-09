using TripPlanner.Domain;

namespace TripPlanner.Application.RouteCalculator;

public interface IQueueSubscriber<T> : IDisposable
{
    Task StartAsync(CancellationToken ct);
}