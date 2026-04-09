using TripPlanner.Application.RouteCalculator;
using TripPlanner.Domain;

namespace TripPlanner.Api.BackgroundServices;

public class ResultSubscriberService(IQueueSubscriber<ComputeResult> subscriber) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await subscriber.StartAsync(stoppingToken);
    }

    public override Task StopAsync(CancellationToken stoppingToken)
    {
        subscriber.Dispose();
        return base.StopAsync(stoppingToken);
    }
}