using TripPlanner.Application.Features.Driver;

namespace TripPlanner.Api.Endpoints.Driver;

// DELETE /driver/routes/{routeId}
// Flow 1.5: driver deletes a route.
// Passengers with accepted rides are notified via RouteDeletedEvent → SSE/push.
//
// 204 No Content      – route deleted
// 401 Unauthorized    – missing X-User-Id header
// 403 Forbidden       – route belongs to a different driver
// 404 Not Found       – route not found
// 409 Conflict        – route is Active and has existing rides (hard block)
public class DeleteRouteEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/driver/routes/{routeId}", async (
            Guid routeId,
            HttpContext http,
            DeleteRouteHandler handler,
            CancellationToken ct) =>
        {
            var driverId = HttpUtils.GetDriverId(http);
            await handler.HandleAsync(new(driverId, routeId), ct);
            return Results.NoContent();
        });
}
