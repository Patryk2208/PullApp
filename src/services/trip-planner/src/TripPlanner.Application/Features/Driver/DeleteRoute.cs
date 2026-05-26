using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;

namespace TripPlanner.Application.Features.Driver;

public record DeleteRouteCommand(Guid DriverId, Guid RouteId);

/// <summary>
/// Flow 1.5 — driver deletes a Route. Three sub-cases:
/// a) Status == Active with rides   → hard block (throw RouteNotDeletableException).
/// b) Status == Created with rides  → delete route, publish RouteDeletedEvent to notify passengers.
/// c) No rides at all               → silent delete.
/// </summary>
public class DeleteRouteHandler(
    IRouteRepository routes,
    IRideRepository rides,
    IEventPublisher events,
    IUnitOfWork uow)
{
    public async Task HandleAsync(DeleteRouteCommand cmd, CancellationToken ct)
    {
        // Flow 1.5
        // 1. Load Route; verify it belongs to the driver.
        // 2. Load all active rides on this route.
        // 3. Case (a): Status == Active and rides exist → throw RouteNotDeletableException.
        // 4. Case (b): Status == Created and rides exist →
        //      a. Delete the route.
        //      b. Commit.
        //      c. Publish RouteDeletedEvent with list of affected passenger IDs.
        // 5. Case (c): no rides → delete route, commit.
        throw new NotImplementedException();
    }
}
