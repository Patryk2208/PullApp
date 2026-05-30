using TripPlanner.Application.Features.Driver;

namespace TripPlanner.Api.Endpoints.Driver;

// POST /driver/requests/{requestId}/reject
// Flow 4: driver rejects a pending RideRequest.
// Passenger funds are unfrozen; passenger notified via RideRejectedEvent → SSE/push.
//
// 204 No Content      – request rejected
// 401 Unauthorized    – missing X-User-Id header
// 403 Forbidden       – request belongs to a different driver's route
// 404 Not Found       – ride request not found
// 409 Conflict        – request is no longer Pending
// 503 Service Unavail – payments service unavailable
public class RejectRideRequestEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/driver/requests/{requestId}/reject", async (
            Guid requestId,
            HttpContext http,
            RejectRideRequestHandler handler,
            CancellationToken ct) =>
        {
            var driverId = HttpUtils.GetDriverId(http);
            await handler.HandleAsync(new(driverId, requestId), ct);
            return Results.NoContent();
        });
}
