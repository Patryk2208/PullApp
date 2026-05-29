using TripPlanner.Application.Features.Driver;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Api.Endpoints.Driver;

// POST /driver/routes
// Flow 0: driver submits a new route. Geometry is computed asynchronously by route-calc.
// Results delivered via SSE/push (RouteReadyEvent) when route-calc responds.
//
// 202 Accepted        – route created, geometry computation queued
// 401 Unauthorized    – missing X-User-Id header
// 403 Forbidden       – driver not authorised to drive (IAccountsService.CanDriveAsync)
// 422 Unprocessable   – start or end point outside active service area
// 503 Service Unavail – accounts service or route-calc unavailable
public class CreateRouteEndpoint : IEndpoint
{
    public record Request(GeoPoint Start, GeoPoint End, int Capacity);
    public record Response(Guid RouteId);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/driver/routes", async (
            Request req,
            HttpContext http,
            CreateRouteHandler handler,
            CancellationToken ct) =>
        {
            var driverId = HttpUtils.GetDriverId(http);
            var result   = await handler.HandleAsync(new(driverId, req.Start, req.End, req.Capacity), ct);
            return Results.Accepted((string?)null, new Response(result.RouteId));
        });
}
