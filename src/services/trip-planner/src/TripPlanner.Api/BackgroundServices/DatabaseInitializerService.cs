using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Infrastructure.Postgres;

namespace TripPlanner.Api.BackgroundServices;

public class DatabaseInitializerService(IEnumerable<ISubscriber> subscribers) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var subscriber in subscribers)
        {
            await subscriber.StartAsync(stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var subscriber in subscribers)
        {
            await subscriber.StopAsync(cancellationToken);
        }
        await base.StopAsync(cancellationToken);
    }
}