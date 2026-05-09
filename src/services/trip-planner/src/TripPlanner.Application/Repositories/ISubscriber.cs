namespace TripPlanner.Application.Repositories;

public interface ISubscriber
{
    Task StartAsync(CancellationToken ct);

    Task StopAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}