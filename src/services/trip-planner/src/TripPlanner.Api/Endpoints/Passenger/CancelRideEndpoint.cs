using TripPlanner.Application.Features.Passenger;

namespace TripPlanner.Api.Endpoints.Passenger;

// DELETE /passenger/rides/{rideId}
// Flow 8 (a or b): passenger cancels a ride before it starts.
// WaitingForActivation → no charge, funds unfrozen.
// WaitingForDriver     → cancellation fee charged, remainder unfrozen.
// Started              → 409 (use POST /passenger/rides/{rideId}/end instead).
// Rejected passengers notified of freed seat via RideEndedEvent → SSE/push.
//
// 204 No Content      – ride cancelled
// 401 Unauthorized    – missing X-User-Id header
// 403 Forbidden       – ride belongs to a different passenger
// 404 Not Found       – ride not found
// 409 Conflict        – ride is Started; use /end endpoint instead
// 503 Service Unavail – payments service unavailable
public class CancelRideEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/passenger/rides/{rideId}", async (
            Guid rideId,
            HttpContext http,
            CancelRideHandler handler,
            CancellationToken ct) =>
        {
            var passengerId = HttpUtils.GetPassengerId(http);
            await handler.HandleAsync(new(passengerId, rideId), ct);
            return Results.NoContent();
        });
}
