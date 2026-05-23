using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Events;

namespace TripPlanner.Application.Features.Driver;

public record CancelRouteCommand(Guid DriverId);

public class CancelRouteHandler(
    IDriverRouteRepository driverRoutes,
    IRideRequestRepository rideRequests,
    IEventPublisher @event,
    ISseHub sseHub)
{
    public async Task HandleAsync(CancelRouteCommand cmd, CancellationToken ct)
    {
        var route = await driverRoutes.GetActiveByDriverIdAsync(cmd.DriverId, ct)
            ?? throw new NotFoundException("no_active_route");

        var activeRide = await rideRequests.GetActiveByPassengerIdAsync(cmd.DriverId, ct);
        if (activeRide is not null)
            throw new CannotModifyDuringRideException();

        route.Cancel();
        await driverRoutes.UpdateAsync(route, ct);

        // Invalidate any passenger requests that still list this driver.
        var affectedRequestIds = await driverRoutes.GetPendingRequestIdsForRouteAsync(route.Id, ct);
        foreach (var requestId in affectedRequestIds)
        {
            var request = await rideRequests.GetByIdAsync(requestId, ct);
            if (request is null) continue;

            var isEmpty = request.RemoveDriverFromResults(route.Id);
            if (isEmpty) request.ReSearch(Guid.NewGuid()); // TODO: re-dispatch compute job
            await rideRequests.UpdateAsync(request, ct);

            var eventType = isEmpty ? "routes_expired" : "routes_ready";
            await sseHub.PushAsync(requestId, eventType,
                System.Text.Json.JsonSerializer.Serialize(new { requestId }), ct);
        }
    }
}
