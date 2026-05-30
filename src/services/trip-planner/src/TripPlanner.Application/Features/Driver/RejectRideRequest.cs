using Microsoft.Extensions.Logging;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Metrics;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Events;
using TripPlanner.Domain.RideRequest;

namespace TripPlanner.Application.Features.Driver;

public record RejectRideRequestCommand(Guid DriverId, Guid RequestId);

/// <summary>
/// Flow 4 — driver rejects a pending RideRequest.
/// </summary>
public class RejectRideRequestHandler(
    IRouteRepository routes,
    IRideRequestRepository rideRequests,
    IPaymentsService payments,
    IEventPublisher events,
    TripPlannerMetrics metrics,
    IUnitOfWork uow,
    ILogger<RejectRideRequestHandler> logger)
{
    public async Task HandleAsync(RejectRideRequestCommand cmd, CancellationToken ct)
    {
        // Flow 4
        // 1. Load RideRequest; verify it is Pending and belongs to the driver's route.
        var request = await rideRequests.GetByIdAsync(cmd.RequestId, ct)
            ?? throw new RideRequestNotFoundException(cmd.RequestId);

        var route = await routes.GetByIdAsync(request.RouteId, ct)
            ?? throw new RouteNotFoundException(request.RouteId);

        if (route.DriverId != cmd.DriverId)
            throw new UnauthorizedException($"RideRequest {cmd.RequestId} belongs to a different driver's route.");

        if (request.Status != RideRequestStatus.Pending)
            throw new InvalidRouteStatusException(
                $"RideRequest {cmd.RequestId} is no longer pending (current: {request.Status}).");

        // 2. Unfreeze the passenger's funds (IPaymentsService.UnfreezeAsync on FrozenPriceId).
        if (request.FrozenPriceId.HasValue)
            await payments.UnfreezeAsync(request.FrozenPriceId.Value, ct);

        // 3. Mark RideRequest as Rejected.
        // 4. Persist and commit.
        request.Reject();
        await rideRequests.UpdateAsync(request, ct);
        await uow.CommitAsync(ct);

        // 5. Publish RideRejectedEvent → notifications service will alert the passenger.
        await events.PublishAsync(Topics.NotificationTriggers,
            new RideRejectedEvent(request.Id, route.Id, route.DriverId, request.PassengerId), ct);

        metrics.RideTransition("pending_request", "rejected", "driver_declined");
        metrics.DriverDeclined("explicit");
        logger.LogInformation("RideRequest rejected requestId={RequestId} passengerId={PassengerId} routeId={RouteId}",
            cmd.RequestId, request.PassengerId, route.Id);
    }
}
