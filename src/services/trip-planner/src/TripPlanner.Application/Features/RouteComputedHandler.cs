using System.Text.Json;
using Microsoft.Extensions.Logging;
using TripPlanner.Application.Metrics;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Application.Features;

public class RouteComputedHandler(
    ISseHub hub,
    TripPlannerMetrics metrics,
    ILogger<RouteComputedHandler> logger) : IHandler<ComputeJobResult>
{
    public async Task HandleAsync(ComputeJobResult result, CancellationToken ct)
    {
        logger.LogDebug("RouteComputed: jobId={JobId} success={Success}", result.JobId, result.Success);

        var json = JsonSerializer.Serialize(result);
        await hub.PushAsync(result.JobId, "drier_route_computed", json, ct);

        metrics.ComputeResultReceived();
        logger.LogInformation("Compute result received for jobId={JobId} success={Success}", result.JobId, result.Success);
    }
}
