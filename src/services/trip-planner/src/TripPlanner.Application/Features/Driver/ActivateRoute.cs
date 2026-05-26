using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Application.Features.Driver;

public record ActivateRouteCommand(Guid DriverId, Guid RouteId, GeoPoint CurrentLocation);

/// <summary>
/// Flow 1 — driver activates a Route (starts driving).
/// </summary>
public class ActivateRouteHandler(
    IRouteRepository routes,
    IRideRepository rides,
    IGeoService geo,
    IUnitOfWork uow)
{
    public async Task HandleAsync(ActivateRouteCommand cmd, CancellationToken ct)
    {
        // Flow 1
        // 1. Load Route; verify it belongs to the driver and Status == Created.
        // 2. Verify driver's CurrentLocation is near Route.Start (IGeoService.IsNearAsync, ~500 m threshold).
        // 3. Call route.Activate(currentLocation) → Status = Active.
        // 4. Load all active rides on this route; call ride.ActivateIfWaiting() on each
        //    so WaitingForActivation → WaitingForDriver.
        // 5. Persist route + updated rides.
        // 6. Commit.
        throw new NotImplementedException();
    }
}
