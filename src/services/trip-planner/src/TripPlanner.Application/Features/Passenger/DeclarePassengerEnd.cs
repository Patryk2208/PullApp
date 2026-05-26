using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;

namespace TripPlanner.Application.Features.Passenger;

public record DeclarePassengerEndCommand(Guid PassengerId, Guid RideId);

/// <summary>
/// Flow 8c (passenger side) — passenger declares the ride is over.
/// Passenger must declare first; driver declaration alone is a no-op.
/// When both parties declare, the ride is completed and payment is charged.
/// </summary>
public class DeclarePassengerEndHandler(
    IRideRepository rides,
    IRouteRepository routes,
    IRideRequestRepository rideRequests,
    IPaymentsService payments,
    IEventPublisher events,
    IUnitOfWork uow)
{
    public async Task HandleAsync(DeclarePassengerEndCommand cmd, CancellationToken ct)
    {
        // Flow 8c — passenger side
        // 1. Load Ride; verify it belongs to the passenger and Status == Started.
        // 2. Call ride.DeclarePassengerEnd() → records the declaration.
        //    Completion happens only when driver also declares (DeclareDriverEnd).
        // 3. Persist and commit.
        //
        // Note: actual ride completion (charging, route seat release, events) happens
        // in DeclareDriverEndHandler once the driver also declares.
        throw new NotImplementedException();
    }
}
