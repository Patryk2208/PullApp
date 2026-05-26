using TripPlanner.Application.Features.Passenger;

namespace TripPlanner.Api.Endpoints.Passenger;

// POST /passenger/rides/{rideId}/end
// Flow 8c (passenger side): passenger declares the ride is over.
// Passenger MUST declare first; the driver's declaration alone is a 403.
// Full completion (payment + events) happens when the driver also calls their /end endpoint.
//
// 204 No Content      – declaration recorded
// 401 Unauthorized    – missing X-User-Id header
// 403 Forbidden       – ride belongs to a different passenger
// 404 Not Found       – ride not found
// 409 Conflict        – ride is not in Started status
public class DeclarePassengerEndEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/passenger/rides/{rideId}/end", async (
            Guid rideId,
            HttpContext http,
            DeclarePassengerEndHandler handler,
            CancellationToken ct) =>
        {
            var passengerId = HttpUtils.GetPassengerId(http);
            await handler.HandleAsync(new(passengerId, rideId), ct);
            return Results.NoContent();
        });
}
