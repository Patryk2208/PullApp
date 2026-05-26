using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;

namespace TripPlanner.Application.Features.Driver;

public record DeclareDriverPickupCommand(Guid DriverId, Guid RideId);

/// <summary>
/// Flow 7 (driver side) — driver declares that they have picked up the passenger.
/// Only after both driver and passenger declare pickup does the Ride start.
/// </summary>
public class DeclareDriverPickupHandler(
    IRideRepository rides,
    IUnitOfWork uow)
{
    public async Task HandleAsync(DeclareDriverPickupCommand cmd, CancellationToken ct)
    {
        // Flow 7 — driver side
        // 1. Load Ride; verify it belongs to the driver and Status == WaitingForDriver.
        // 2. Call ride.DeclareDriverPickup().
        //    - If both parties have now declared → Status becomes Started (handled inside domain).
        // 3. Persist and commit.
        throw new NotImplementedException();
    }
}
