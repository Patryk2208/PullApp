using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TripPlanner.Application.Features;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Infrastructure.Fakes
{
    public class FakeComputeWorker(
    FakeComputeQueue queue,
    IServiceScopeFactory scopeFactory)
    : BackgroundService
    {
        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            await foreach (
                var job
                in queue.Queue.Reader.ReadAllAsync(stoppingToken))
            {
                await Task.Delay(500, stoppingToken);

                using var scope =
                    scopeFactory.CreateScope();

                var handler =
                    scope.ServiceProvider
                        .GetRequiredService<RouteComputedHandler>();

                // todo fix
                // await handler.HandleAsync(
                //     job,
                //     stoppingToken);
            }
        }
    }
}
