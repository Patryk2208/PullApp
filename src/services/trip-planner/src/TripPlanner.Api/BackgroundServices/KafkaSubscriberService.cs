using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;

namespace TripPlanner.Api.BackgroundServices;

// Hosts the KafkaConsumerService<string> as an ASP.NET BackgroundService.
public class KafkaSubscriberService(ISubscriber subscriber) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await subscriber.StartAsync(stoppingToken);
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        await subscriber.StopAsync(stoppingToken);
        await base.StopAsync(stoppingToken);
    }
}
