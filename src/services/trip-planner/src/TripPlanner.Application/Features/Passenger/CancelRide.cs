using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Events;
using TripPlanner.Domain.Ride;

namespace TripPlanner.Application.Features.Passenger;

public record CancelRideCommand(Guid PassengerId, Guid RideId);

/// <summary>
/// Flow 8 (passenger-initiated) — passenger cancels a Ride.
/// Three sub-cases depending on the current Ride status:
/// a) WaitingForActivation → no charge, funds unfrozen.
/// b) WaitingForDriver     → cancellation fee charged, remainder unfrozen.
/// c) Started              → use DeclarePassengerEnd instead.
/// </summary>
public class CancelRideHandler(
    IRideRepository rides,
    IRouteRepository routes,
    IRideRequestRepository rideRequests,
    IPaymentsService payments,
    IEventPublisher events,
    IUnitOfWork uow)
{
    public async Task HandleAsync(CancelRideCommand cmd, CancellationToken ct)
    {
        // Flow 8
        // 1. Load Ride; verify it belongs to the passenger.
        var ride = await rides.GetByIdAsync(cmd.RideId, ct)
            ?? throw new RideNotFoundException(cmd.RideId);

        if (ride.PassengerId != cmd.PassengerId)
            throw new UnauthorizedException($"Ride {cmd.RideId} belongs to a different passenger.");

        // 4. Case (c) Status == Started → reject; use /end endpoint instead.
        if (ride.Status == RideStatus.Started)
            throw new InvalidRouteStatusException(
                "Ride is already in progress; use the /end endpoint to declare the ride over.");

        // 2. Case (a) Status == WaitingForActivation: no charge, unfreeze funds.
        // 3. Case (b) Status == WaitingForDriver: charge cancellation fee.
        if (ride.Status == RideStatus.WaitingForActivation)
        {
            if (ride.FrozenPriceId.HasValue)
                await payments.UnfreezeAsync(ride.FrozenPriceId.Value, ct);
        }
        else // WaitingForDriver
        {
            if (ride.FrozenPriceId.HasValue)
                await payments.ChargeCancellationAsync(ride.FrozenPriceId.Value, ride.CancellationPrice, ct);
        }

        // 5. Load Route; call route.RemoveRide() → Status may revert Active from Full.
        var route = await routes.GetByIdAsync(ride.RouteId, ct);
        if (route is not null)
        {
            route.RemoveRide();
            await routes.UpdateAsync(route, ct);
        }

        // 6. Persist ride (mark ended) and commit.
        ride.Cancel();
        await rides.UpdateAsync(ride, ct);
        await uow.CommitAsync(ct);

        // 7. Load all previously Rejected RideRequests for this route.
        var rejectedRequests = await rideRequests.GetRejectedByRouteIdAsync(ride.RouteId, ct);
        var notifyPassengerIds = rejectedRequests.Select(r => r.PassengerId).ToList();

        // 8. Publish RideEndedEvent (notifies rejected passengers that a seat may be free).
        await events.PublishAsync(Topics.NotificationTriggers,
            new RideEndedEvent(ride.Id, ride.RouteId, ride.DriverId, ride.PassengerId, notifyPassengerIds), ct);

        // 9. Publish RideCancelledEvent (billing/audit trail).
        await events.PublishAsync(Topics.RideCompletions,
            new RideCancelledEvent(ride.Id, ride.DriverId, ride.PassengerId,
                ride.FrozenPriceId, "passenger", ride.EndedAt!.Value), ct);
    }
}
