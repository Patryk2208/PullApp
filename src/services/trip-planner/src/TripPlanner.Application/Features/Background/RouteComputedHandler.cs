using System.Text.Json;
using Microsoft.Extensions.Logging;
using TripPlanner.Application.Metrics;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Events;
using TripPlanner.Domain.Route;

namespace TripPlanner.Application.Features.Background;

/// <summary>
/// Background handler for all RabbitMQ compute results (both job types).
///
/// DriverRouteComputeResult  → completes flow 0: sets geometry on Route (Status = Created),
///                             then publishes RouteReadyEvent so the notifications service
///                             delivers SSE/push to the driver.
///
/// PassengerMatchComputeResult → completes flow 2: publishes RouteSearchCompletedEvent so
///                               the notifications service pushes the match list to the passenger.
///
/// FailedComputeResult         → marks the RouteJob as Failed; the notifications service can
///                               optionally push an error notification.
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
            case DriverRouteComputeResult r:
                await HandleDriverRouteAsync(r, ct);
                break;

            case PassengerMatchComputeResult r:
                await HandlePassengerMatchAsync(r, ct);
                break;

            case FailedComputeResult r:
                await HandleFailureAsync(r, ct);
                break;
        }
    }

    private async Task HandleDriverRouteAsync(DriverRouteComputeResult r, CancellationToken ct)
    {
        // Flow 0 — completion
        // 1. Look up RouteJob by CorrelationId (== message.JobId).
        var job = await jobs.GetByCorrelationIdAsync(r.JobId, ct);
        if (job is null) { logger.LogWarning("DriverRoute result for unknown correlationId={CorrelationId} — discarding", r.JobId); return; }

        var route = await routes.GetActiveByDriverIdAsync(job.RequesterId, ct);
        if (route is null || route.Status != RouteStatus.Calculating)
        {
            logger.LogWarning("DriverRoute result for correlationId={CorrelationId} but route not in Calculating state — discarding", r.JobId);
            return;
        }

        // 3. Call route.SetGeometry(json, eta, distance) → Status = Created.
        route.SetGeometry(r.Result.RouteGeomJson, r.Result.EtaSeconds, r.Result.DistanceMeters);

        // 4. Mark RouteJob as Completed (store geometry JSON for audit).
        job.Complete(r.Result.RouteGeomJson);

        // 5. Persist both and commit.
        await routes.UpdateAsync(route, ct);
        await jobs.UpdateAsync(job, ct);
        await uow.CommitAsync(ct);

        // 6. Publish RouteReadyEvent → notifications service SSE/push to driver.
        await events.PublishAsync(Topics.NotificationTriggers,
            new RouteReadyEvent(route.Id, route.DriverId,
                r.Result.RouteGeomJson, r.Result.EtaSeconds, r.Result.DistanceMeters), ct);

        metrics.RecordRouteCalcResult(r.JobId, "success");
        metrics.DriverRouteRegistrationCompleted();
        logger.LogInformation("DriverRoute computed routeId={RouteId} driverId={DriverId} etaSeconds={Eta} distanceMeters={Distance}",
            route.Id, route.DriverId, r.Result.EtaSeconds, r.Result.DistanceMeters);
    }

    private async Task HandlePassengerMatchAsync(PassengerMatchComputeResult r, CancellationToken ct)
    {
        var job = await jobs.GetByCorrelationIdAsync(r.JobId, ct);
        if (job is null) { logger.LogWarning("PassengerMatch result for unknown correlationId={CorrelationId} — discarding", r.JobId); return; }

        // 2. Serialize r.Result into RouteJob.ResultJson; mark Completed.
        job.Complete(JsonSerializer.Serialize(r.Result));

        // 3. Persist and commit.
        await jobs.UpdateAsync(job, ct);
        await uow.CommitAsync(ct);

        // 4. Publish RouteSearchCompletedEvent (JobId, PassengerId, Matches)
        //    → notifications service SSE/push to passenger.
        await events.PublishAsync(Topics.NotificationTriggers,
            new RouteSearchCompletedEvent(job.Id, job.RequesterId, r.Result.Matches), ct);

        var outcome = r.Result.Matches.Count == 0 ? "no_drivers" : "matched";
        metrics.RecordMatchingJobResult(r.JobId, outcome);
        metrics.RecordRouteCalcResult(r.JobId, "success");
        logger.LogInformation("PassengerMatch computed passengerId={PassengerId} matches={MatchCount} outcome={Outcome}",
            job.RequesterId, r.Result.Matches.Count, outcome);
    }

    private async Task HandleFailureAsync(FailedComputeResult r, CancellationToken ct)
    {
        var job = await jobs.GetByCorrelationIdAsync(r.JobId, ct);
        if (job is null) { logger.LogWarning("Compute failure for unknown correlationId={CorrelationId} — discarding", r.JobId); return; }

        // 2. Call job.Fail(r.Error).
        job.Fail(r.Error ?? "Unknown compute error.");

        // 3. Persist and commit.
        await jobs.UpdateAsync(job, ct);
        await uow.CommitAsync(ct);

        metrics.RecordRouteCalcResult(r.JobId, "error");
        if (r.JobType == JobType.PassengerMatch)
            metrics.RecordMatchingJobResult(r.JobId, "error");
        else
            metrics.DriverRouteRegistrationFailed();
        logger.LogWarning("Compute job failed correlationId={CorrelationId} jobType={JobType} error={Error}",
            r.JobId, r.JobType, r.Error);
    }
}
