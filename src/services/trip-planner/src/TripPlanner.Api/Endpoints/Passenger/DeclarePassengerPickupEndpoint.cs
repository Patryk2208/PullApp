using TripPlanner.Application.Features.Passenger;

namespace TripPlanner.Api.Endpoints.Passenger;

// POST /passenger/rides/{rideId}/pickup
// Flow 7 (passenger side): passenger confirms they've been picked up by the driver.
// Driver must declare pickup first; out-of-order declaration returns 403.
//
// 204 No Content      – declaration recorded; ride transitions to Started if driver already declared
// 401 Unauthorized    – missing X-User-Id header
// 403 Forbidden       – ride belongs to a different passenger, or driver hasn't declared pickup yet
// 404 Not Found       – ride not found
// 409 Conflict        – ride is not in WaitingForDriver status
public class DeclarePassengerPickupEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/passenger/rides/{rideId}/pickup", async (
            Guid rideId,
            HttpContext http,
            DeclarePassengerPickupHandler handler,
            CancellationToken ct) =>
        {
            var passengerId = HttpUtils.GetPassengerId(http);
            await handler.HandleAsync(new(passengerId, rideId), ct);
            return Results.NoContent();
        });
}
