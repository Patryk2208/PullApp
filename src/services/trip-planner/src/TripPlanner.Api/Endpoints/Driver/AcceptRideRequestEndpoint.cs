using TripPlanner.Application.Features.Driver;

namespace TripPlanner.Api.Endpoints.Driver;

// POST /driver/requests/{requestId}/accept
// Flow 5: driver accepts a pending RideRequest.
// Atomically creates a Ride and potentially marks the route Full.
// If route became Full, all remaining pending requests are auto-rejected.
// A chat room is opened and the passenger is notified via RideAcceptedEvent → SSE/push.
//
// 200 OK              – { rideId, chatRoomId }
// 401 Unauthorized    – missing X-User-Id header
// 403 Forbidden       – request belongs to a different driver's route
// 404 Not Found       – ride request not found
// 409 Conflict        – route is already Full, or request is no longer Pending
// 503 Service Unavail – payments or chat service unavailable
public class AcceptRideRequestEndpoint : IEndpoint
{
    public record Response(Guid RideId, Guid? ChatRoomId);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/driver/requests/{requestId}/accept", async (
            Guid requestId,
            HttpContext http,
            AcceptRideRequestHandler handler,
            CancellationToken ct) =>
        {
            var driverId = HttpUtils.GetDriverId(http);
            var result   = await handler.HandleAsync(new(driverId, requestId), ct);
            return Results.Ok(new Response(result.RideId, result.ChatRoomId));
        });
}
