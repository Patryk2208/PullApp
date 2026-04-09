using TripPlanner.Application.RouteCalculator;
using TripPlanner.Domain;

namespace TripPlanner.Api.BackgroundServices;

public class ResultSubscriberService : BackgroundService
{
    private readonly IQueueSubscriber<ComputeResult> _subscriber;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _subscriber.StartAsync(stoppingToken);
    }

    public override Task StopAsync(CancellationToken stoppingToken)
    {
        _subscriber.Dispose();
        return base.StopAsync(stoppingToken);
    }
}