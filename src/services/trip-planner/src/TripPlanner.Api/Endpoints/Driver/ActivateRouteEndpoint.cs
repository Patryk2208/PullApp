using TripPlanner.Application.Features.Driver;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Api.Endpoints.Driver;

// POST /driver/routes/{routeId}/activate
// Flow 1: driver signals they are at the start location and ready to pick up passengers.
//
// 204 No Content      – route activated; WaitingForActivation rides transition to WaitingForDriver
// 401 Unauthorized    – missing X-User-Id header
// 403 Forbidden       – route belongs to a different driver
// 404 Not Found       – route not found
// 409 Conflict        – route not in Created status
// 422 Unprocessable   – driver's current location is not near Route.Start
public class ActivateRouteEndpoint : IEndpoint
{
    public record Request(GeoPoint CurrentLocation);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/driver/routes/{routeId}/activate", async (
            Guid routeId,
            Request req,
            HttpContext http,
            ActivateRouteHandler handler,
            CancellationToken ct) =>
        {
            var driverId = HttpUtils.GetDriverId(http);
            await handler.HandleAsync(new(driverId, routeId, req.CurrentLocation), ct);
            return Results.NoContent();
        });
}
