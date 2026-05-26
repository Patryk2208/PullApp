using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Events;

namespace TripPlanner.Application.Features.Driver;

public record DeclareDriverEndCommand(Guid DriverId, Guid RideId);

/// <summary>
/// Flow 8c (driver side) — driver declares the end of a Started ride.
/// Driver declaration is ignored unless the passenger has already declared end.
/// If both parties declared, the ride is completed and payment is charged.
/// </summary>
public class DeclareDriverEndHandler(
    IRideRepository rides,
    IRouteRepository routes,
    IRideRequestRepository rideRequests,
    IPaymentsService payments,
    IEventPublisher events,
    IUnitOfWork uow)
{
    public async Task HandleAsync(DeclareDriverEndCommand cmd, CancellationToken ct)
    {
        // Flow 8c — driver side
        // 1. Load Ride; verify it belongs to the driver.
        // 2. Call ride.DeclareDriverEnd().
        //    - Returns false if passenger hasn't declared yet → throw DeclarationOrderException (403).
        // 3. ride.IsEnded is true (both declared):
        //    a. Load Route; call route.RemoveRide() → Status may revert Active from Full.
        //    b. Charge the passenger (IPaymentsService.ChargeAsync on FrozenPriceId).
        //    c. Persist ride and route; commit.
        //    d. Load all previously Rejected RideRequests for this route (to notify them a seat opened).
        //    e. Publish RideEndedEvent (notifies rejected passengers that a seat may be free).
        //    f. Publish RideCompletedEvent (billing confirmation to payments service).
        throw new NotImplementedException();
    }
}
