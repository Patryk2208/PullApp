using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Compute;

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
        // 2. Find the Calculating Route for RouteJob.RequesterId (DriverId).
        // 3. Call route.SetGeometry(json, eta, distance) → Status = Created.
        // 4. Mark RouteJob as Completed (resultJson = geometry JSON for audit).
        // 5. Persist both and commit.
        // 6. Publish RouteReadyEvent → notifications service SSE/push to driver.
        throw new NotImplementedException();
    }

    private async Task HandlePassengerMatchAsync(PassengerMatchComputeResult r, CancellationToken ct)
    {
        // Flow 2 — completion
        // 1. Look up RouteJob by CorrelationId.
        // 2. Serialize r.Result into RouteJob.ResultJson; mark Completed.
        // 3. Persist and commit.
        // 4. Publish RouteSearchCompletedEvent (JobId, PassengerId, Matches)
        //    → notifications service SSE/push to passenger.
        throw new NotImplementedException();
    }

    private async Task HandleFailureAsync(FailedComputeResult r, CancellationToken ct)
    {
        // 1. Look up RouteJob by CorrelationId.
        // 2. Call job.Fail(r.Error).
        // 3. Persist and commit.
        // (Optionally publish a failure event for notifications service to inform the user.)
        throw new NotImplementedException();
    }
}
