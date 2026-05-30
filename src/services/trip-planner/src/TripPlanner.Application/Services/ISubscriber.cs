namespace TripPlanner.Application.Services;

public interface ISubscriber
{
    Task StartAsync(CancellationToken ct);

    Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
