using System.Text.Json;
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
    IUnitOfWork uow) : IHandler<ComputeJobResult>
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
        if (job is null) return; // stale or duplicate message

        // 2. Find the Calculating Route for RouteJob.RequesterId (DriverId).
        var route = await routes.GetActiveByDriverIdAsync(job.RequesterId, ct);
        if (route is null || route.Status != RouteStatus.Calculating) return;

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
    }

    private async Task HandlePassengerMatchAsync(PassengerMatchComputeResult r, CancellationToken ct)
    {
        // Flow 2 — completion
        // 1. Look up RouteJob by CorrelationId.
        var job = await jobs.GetByCorrelationIdAsync(r.JobId, ct);
        if (job is null) return;

        // 2. Serialize r.Result into RouteJob.ResultJson; mark Completed.
        job.Complete(JsonSerializer.Serialize(r.Result));

        // 3. Persist and commit.
        await jobs.UpdateAsync(job, ct);
        await uow.CommitAsync(ct);

        // 4. Publish RouteSearchCompletedEvent (JobId, PassengerId, Matches)
        //    → notifications service SSE/push to passenger.
        await events.PublishAsync(Topics.NotificationTriggers,
            new RouteSearchCompletedEvent(job.Id, job.RequesterId, r.Result.Matches), ct);
    }

    private async Task HandleFailureAsync(FailedComputeResult r, CancellationToken ct)
    {
        // 1. Look up RouteJob by CorrelationId.
        var job = await jobs.GetByCorrelationIdAsync(r.JobId, ct);
        if (job is null) return;

        // 2. Call job.Fail(r.Error).
        job.Fail(r.Error ?? "Unknown compute error.");

        // 3. Persist and commit.
        await jobs.UpdateAsync(job, ct);
        await uow.CommitAsync(ct);
    }
}
