using Microsoft.Extensions.Logging;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;

namespace TripPlanner.Application.Features.Driver;

public record CancelRouteCommand(Guid DriverId);

public class CancelRouteHandler(
    IDriverRouteRepository driverRoutes,
    IRideRequestRepository rideRequests,
    ISseHub sseHub,
    ILogger<CancelRouteHandler> logger)
{
    public async Task HandleAsync(CancelRouteCommand cmd, CancellationToken ct)
    {
        logger.LogDebug("CancelRoute: driverId={DriverId}", cmd.DriverId);

        var route = await driverRoutes.GetActiveByDriverIdAsync(cmd.DriverId, ct)
            ?? throw new NotFoundException("no_active_route");

        var activeRide = await rideRequests.GetActiveByPassengerIdAsync(cmd.DriverId, ct);
        if (activeRide is not null)
        {
            logger.LogWarning("CancelRoute: driverId={DriverId} has active ride, cannot cancel", cmd.DriverId);
            throw new CannotModifyDuringRideException();
        }

        route.Cancel();
        await driverRoutes.UpdateAsync(route, ct);
        logger.LogDebug("CancelRoute: route={RouteId} cancelled", route.Id);

        var affectedRequestIds = await driverRoutes.GetPendingRequestIdsForRouteAsync(route.Id, ct);
        logger.LogDebug("CancelRoute: invalidating {Count} affected passenger requests", affectedRequestIds.Count());
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

        logger.LogInformation("Driver {DriverId} cancelled route={RouteId}", cmd.DriverId, route.Id);
    }
}
