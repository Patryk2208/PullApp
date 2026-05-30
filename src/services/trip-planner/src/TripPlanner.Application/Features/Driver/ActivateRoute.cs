using Microsoft.Extensions.Logging;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Route;

namespace TripPlanner.Application.Features.Driver;

public record ActivateRouteCommand(Guid DriverId, Guid RouteId, GeoPoint CurrentLocation);

/// <summary>
/// Flow 1 — driver activates a Route (starts driving).
/// </summary>
public class ActivateRouteHandler(
    IRouteRepository routes,
    IRideRepository rides,
    IGeoService geo,
    IUnitOfWork uow,
    ILogger<ActivateRouteHandler> logger)
{
    private const double NearStartThresholdMeters = 500;

    public async Task HandleAsync(ActivateRouteCommand cmd, CancellationToken ct)
    {
        // Flow 1
        // 1. Load Route; verify it belongs to the driver and Status == Created.
        var route = await routes.GetByIdAsync(cmd.RouteId, ct)
            ?? throw new RouteNotFoundException(cmd.RouteId);

        if (route.DriverId != cmd.DriverId)
            throw new UnauthorizedException($"Route {cmd.RouteId} belongs to a different driver.");

        if (route.Status != RouteStatus.Created)
            throw new InvalidRouteStatusException(
                $"Route must be in Created status to activate (current: {route.Status}).");

        // 2. Verify driver's CurrentLocation is near Route.Start (IGeoService.IsNearAsync, ~500 m threshold).
        if (!await geo.IsNearAsync(cmd.CurrentLocation, route.Start, NearStartThresholdMeters, ct))
            throw new OutsideServiceAreaException(
                $"Driver location must be within {NearStartThresholdMeters} m of the route start point.");

        // 3. Call route.Activate(currentLocation) → Status = Active.
        route.Activate(cmd.CurrentLocation);

        // 4. Load all active rides on this route; call ride.ActivateIfWaiting() on each
        //    so WaitingForActivation → WaitingForDriver.
        var activeRides = await rides.GetActiveByRouteIdAsync(route.Id, ct);
        foreach (var ride in activeRides)
            ride.ActivateIfWaiting();

        // 5. Persist route + updated rides.
        // 6. Commit.
        await uow.BeginAsync(ct);
        await routes.UpdateAsync(route, ct);
        foreach (var ride in activeRides)
            await rides.UpdateAsync(ride, ct);
        await uow.CommitAsync(ct);
        logger.LogInformation("Route activated routeId={RouteId} driverId={DriverId} ridesActivated={Count}",
            route.Id, cmd.DriverId, activeRides.Count);
    }
}
