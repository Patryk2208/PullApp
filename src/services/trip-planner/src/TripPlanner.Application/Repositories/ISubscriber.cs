namespace TripPlanner.Application.Repositories;

public interface ISubscriber<T> : IDisposable
{
    Task StartAsync(CancellationToken ct);
}