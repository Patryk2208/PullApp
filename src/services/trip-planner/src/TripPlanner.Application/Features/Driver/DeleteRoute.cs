using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Events;
using TripPlanner.Domain.Route;

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
    IRideRequestRepository rideRequests,
    IPaymentsService payments,
    IEventPublisher events,
    IUnitOfWork uow)
{
    public async Task HandleAsync(DeleteRouteCommand cmd, CancellationToken ct)
    {
        // Flow 1.5
        // 1. Load Route; verify it belongs to the driver.
        var route = await routes.GetByIdAsync(cmd.RouteId, ct)
            ?? throw new RouteNotFoundException(cmd.RouteId);

        if (route.DriverId != cmd.DriverId)
            throw new UnauthorizedException($"Route {cmd.RouteId} belongs to a different driver.");

        // 2. Load all active rides on this route.
        var activeRides = await rides.GetActiveByRouteIdAsync(route.Id, ct);

        // 3. Case (a): Status == Active and rides exist → hard block.
        if (route.Status == RouteStatus.Active && activeRides.Count > 0)
            throw new RouteNotDeletableException(route.Id);

        // Regardless of sub-case: reject and unfreeze any pending RideRequests so no
        // passenger funds remain frozen after the route disappears.
        var pendingRequests = await rideRequests.GetPendingByRouteIdAsync(route.Id, ct);
        foreach (var req in pendingRequests)
        {
            if (req.FrozenPriceId.HasValue)
                await payments.UnfreezeAsync(req.FrozenPriceId.Value, ct);
            req.Reject();
        }

        // Collect affected passenger IDs before deletion (for the event in case b).
        var affectedPassengerIds = activeRides.Select(r => r.PassengerId).ToList();

        // 4. Case (b): Status == Created and rides exist — unfreeze ride payments.
        foreach (var ride in activeRides)
            if (ride.FrozenPriceId.HasValue)
                await payments.UnfreezeAsync(ride.FrozenPriceId.Value, ct);

        // 5. Delete: persist rejected requests, then delete rides and the route; commit.
        await uow.BeginAsync(ct);
        foreach (var req in pendingRequests)
            await rideRequests.UpdateAsync(req, ct);
        await rides.DeleteByRouteIdAsync(route.Id, ct);
        await routes.DeleteAsync(route.Id, ct);
        await uow.CommitAsync(ct);

        // 6. Publish RouteDeletedEvent only when there were accepted rides (case b).
        if (affectedPassengerIds.Count > 0)
            await events.PublishAsync(Topics.NotificationTriggers,
                new RouteDeletedEvent(route.Id, route.DriverId, affectedPassengerIds), ct);
    }
}
