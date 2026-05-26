using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;

namespace TripPlanner.Application.Features.Passenger;

public record DeclarePassengerPickupCommand(Guid PassengerId, Guid RideId);

/// <summary>
/// Flow 7 (passenger side) — passenger confirms they have been picked up.
/// Ignored (no-op) if the driver has not yet declared pickup.
/// Both declarations are required before Status transitions to Started.
/// </summary>
public class DeclarePassengerPickupHandler(
    IRideRepository rides,
    IUnitOfWork uow)
{
    public async Task HandleAsync(DeclarePassengerPickupCommand cmd, CancellationToken ct)
    {
        // Flow 7 — passenger side
        // 1. Load Ride; verify it belongs to the passenger.
        // 2. Call ride.DeclarePassengerPickup().
        //    - Returns false if driver hasn't declared yet → throw DeclarationOrderException (403).
        //    - Returns true and Status becomes Started when both parties have declared.
        // 3. Persist and commit.
        throw new NotImplementedException();
    }
}
