using TripPlanner.Application.Features.Driver;

namespace TripPlanner.Api.Endpoints.Driver;

// POST /driver/rides/{rideId}/pickup
// Flow 7 (driver side): driver declares the passenger has been picked up.
// Ride transitions to Started only after the passenger also declares pickup.
//
// 204 No Content      – declaration recorded
// 401 Unauthorized    – missing X-User-Id header
// 403 Forbidden       – ride belongs to a different driver
// 404 Not Found       – ride not found
// 409 Conflict        – ride is not in WaitingForDriver status
public class DeclareDriverPickupEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/driver/rides/{rideId}/pickup", async (
            Guid rideId,
            HttpContext http,
            DeclareDriverPickupHandler handler,
            CancellationToken ct) =>
        {
            var driverId = HttpUtils.GetDriverId(http);
            await handler.HandleAsync(new(driverId, rideId), ct);
            return Results.NoContent();
        });
}
