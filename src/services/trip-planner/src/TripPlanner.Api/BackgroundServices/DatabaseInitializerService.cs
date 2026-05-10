using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Infrastructure.Postgres;

namespace TripPlanner.Api.BackgroundServices;

public class DatabaseInitializerService(ISubscriber subscriber) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await subscriber.StartAsync(stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await subscriber.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}