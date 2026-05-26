using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;

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
        // 2. Case (a) Status == WaitingForActivation:
        //      - Unfreeze funds (IPaymentsService.UnfreezeAsync).
        // 3. Case (b) Status == WaitingForDriver:
        //      - Charge cancellation fee (IPaymentsService.ChargeCancellationAsync).
        // 4. Case (c) Status == Started → throw (use DeclarePassengerEnd endpoint instead).
        //
        // Common teardown (cases a & b):
        // 5. Load Route; call route.RemoveRide() → Status may revert Active from Full.
        // 6. Persist ride (mark ended/removed) and updated route; commit.
        // 7. Load all previously Rejected RideRequests for this route.
        // 8. Publish RideEndedEvent (notifies rejected passengers that a seat may be free).
        // 9. Publish RideCancelledEvent (billing/audit trail).
        throw new NotImplementedException();
    }
}
