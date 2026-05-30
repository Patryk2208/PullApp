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
        await hub.PushAsync(result.JobId, "driver_route_computed", json, ct);

        metrics.ComputeResultReceived();

        var calcResult = result.Success ? "success" : "error";
        metrics.RecordRouteCalcResult(result.JobId, calcResult);

        switch (result)
        {
            case PassengerMatchComputeResult r:
                var outcome = r.Result.Matches.Count == 0 ? "no_drivers" : "matched";
                metrics.MatchingResultRecorded(outcome);
                metrics.RecordMatchingJobResult(result.JobId, outcome);
                if (outcome == "no_drivers")
                    metrics.MatchingNoDriversFound();
                break;

            case DriverRouteComputeResult:
                metrics.DriverRouteCompleted(result.JobId);
                break;

            case FailedComputeResult f:
                if (f.JobType == JobType.PassengerMatch)
                {
                    metrics.MatchingResultRecorded("error");
                    metrics.RecordMatchingJobResult(result.JobId, "error");
                }
                else
                {
                    metrics.DriverRouteFailed(result.JobId);
                }
                break;
        }

        logger.LogInformation("Compute result received for jobId={JobId} success={Success}", result.JobId, result.Success);
    }
}
