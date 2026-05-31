using System.Text.Json;
using Microsoft.Extensions.Logging;
using TripPlanner.Application.Metrics;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Events;
using TripPlanner.Domain.Route;

namespace TripPlanner.Application.Features.Background;

/// <summary>
/// Background handler for all RabbitMQ compute results (both job types).
///
/// BestRouteComputeResult    → completes flow 0: sets geometry on Route (Status = Created),
///                             then publishes RouteReadyEvent so the notifications service
///                             delivers SSE/push to the driver.
///
/// RideMatchingComputeResult → completes flow 2: publishes RouteSearchCompletedEvent so
///                             the notifications service pushes the match list to the passenger.
///
/// FailedComputeResult       → marks the RouteJob as Failed.
/// </summary>
public class RouteComputedHandler(
    IRouteJobRepository jobs,
    IRouteRepository routes,
    IEventPublisher events,
    TripPlannerMetrics metrics,
    IUnitOfWork uow,
    ILogger<RouteComputedHandler> logger) : IHandler<ComputeJobResult>
{
    public async Task HandleAsync(ComputeJobResult message, CancellationToken ct)
    {
        switch (message)
        {
            case BestRouteComputeResult r:
                await HandleBestRouteAsync(r, ct);
                break;

            case RideMatchingComputeResult r:
                await HandleRideMatchingAsync(r, ct);
                break;

            case FailedComputeResult r:
                await HandleFailureAsync(r, ct);
                break;
        }
    }

    private async Task HandleBestRouteAsync(BestRouteComputeResult r, CancellationToken ct)
    {
        var job = await jobs.GetByCorrelationIdAsync(r.JobId, ct);
        if (job is null) { logger.LogWarning("BestRoute result for unknown correlationId={CorrelationId} — discarding", r.JobId); return; }

        var route = await routes.GetActiveByDriverIdAsync(job.RequesterId, ct);
        if (route is null || route.Status != RouteStatus.Calculating)
        {
            logger.LogWarning("BestRoute result for correlationId={CorrelationId} but route not in Calculating state — discarding", r.JobId);
            return;
        }

        route.SetGeometry(r.Result.Points, r.Result.DurationSeconds, r.Result.DistanceMeters);

        job.Complete("{}");

        await routes.UpdateAsync(route, ct);
        await jobs.UpdateAsync(job, ct);
        await uow.CommitAsync(ct);

        await events.PublishAsync(Topics.NotificationTriggers,
            new RouteReadyEvent(route.Id, route.DriverId, route.RoutePoints!, r.Result.DistanceMeters, r.Result.DurationSeconds), ct);

        metrics.RecordRouteCalcResult(r.JobId, "success");
        metrics.DriverRouteRegistrationCompleted();
        logger.LogInformation("BestRoute computed routeId={RouteId} driverId={DriverId} distanceMeters={Distance} durationSeconds={Duration}",
            route.Id, route.DriverId, r.Result.DistanceMeters, r.Result.DurationSeconds);
    }

    private async Task HandleRideMatchingAsync(RideMatchingComputeResult r, CancellationToken ct)
    {
        var job = await jobs.GetByCorrelationIdAsync(r.JobId, ct);
        if (job is null) { logger.LogWarning("RideMatching result for unknown correlationId={CorrelationId} — discarding", r.JobId); return; }

        job.Complete(JsonSerializer.Serialize(r.Result));

        await jobs.UpdateAsync(job, ct);
        await uow.CommitAsync(ct);

        await events.PublishAsync(Topics.NotificationTriggers,
            new RouteSearchCompletedEvent(job.Id, job.RequesterId, r.Result.Matches), ct);

        var outcome = r.Result.Matches.Count == 0 ? "no_drivers" : "matched";
        metrics.RecordMatchingJobResult(r.JobId, outcome);
        metrics.RecordRouteCalcResult(r.JobId, "success");
        logger.LogInformation("RideMatching computed passengerId={PassengerId} matches={MatchCount} outcome={Outcome}",
            job.RequesterId, r.Result.Matches.Count, outcome);
    }

    private async Task HandleFailureAsync(FailedComputeResult r, CancellationToken ct)
    {
        var job = await jobs.GetByCorrelationIdAsync(r.JobId, ct);
        if (job is null) { logger.LogWarning("Compute failure for unknown correlationId={CorrelationId} — discarding", r.JobId); return; }

        job.Fail(r.Error ?? "Unknown compute error.");

        await jobs.UpdateAsync(job, ct);
        await uow.CommitAsync(ct);

        metrics.RecordRouteCalcResult(r.JobId, "error");
        if (r.JobType == JobType.RideMatching)
            metrics.RecordMatchingJobResult(r.JobId, "error");
        else
            metrics.DriverRouteRegistrationFailed();
        logger.LogWarning("Compute job failed correlationId={CorrelationId} jobType={JobType} error={Error}",
            r.JobId, r.JobType, r.Error);
    }

}