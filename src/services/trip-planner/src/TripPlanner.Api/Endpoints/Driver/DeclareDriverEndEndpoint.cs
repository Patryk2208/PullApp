using TripPlanner.Application.Features.Driver;

namespace TripPlanner.Api.Endpoints.Driver;

// POST /driver/rides/{rideId}/end
// Flow 8c (driver side): driver declares the ride is over.
// Passenger must declare end first; out-of-order declaration returns 403.
// When both parties declare: payment charged, RideCompletedEvent + RideEndedEvent published.
//
// 204 No Content      – both declared; ride completed and payment charged
// 401 Unauthorized    – missing X-User-Id header
// 403 Forbidden       – ride belongs to a different driver, or passenger hasn't declared end yet
// 404 Not Found       – ride not found
// 409 Conflict        – ride is not in Started status
// 503 Service Unavail – payments service unavailable
public class DeclareDriverEndEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/driver/rides/{rideId}/end", async (
            Guid rideId,
            HttpContext http,
            DeclareDriverEndHandler handler,
            CancellationToken ct) =>
        {
            var driverId = HttpUtils.GetDriverId(http);
            await handler.HandleAsync(new(driverId, rideId), ct);
            return Results.NoContent();
        });
}
