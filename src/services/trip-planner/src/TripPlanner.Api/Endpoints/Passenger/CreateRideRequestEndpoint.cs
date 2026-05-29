using TripPlanner.Application.Features.Passenger;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Api.Endpoints.Passenger;

// POST /passenger/routes/{routeId}/requests
// Flow 3: passenger chooses a route and creates a RideRequest.
// Price is quoted and funds are frozen atomically by the payments service.
// Driver is notified via RideRequestedEvent → SSE/push.
//
// 201 Created         – { requestId }
// 401 Unauthorized    – missing X-User-Id header
// 404 Not Found       – route not found
// 409 Conflict        – route is Full
// 422 Unprocessable   – passenger start/end outside active service area
// 503 Service Unavail – payments service unavailable
public class CreateRideRequestEndpoint : IEndpoint
{
    public record Request(GeoPoint Start, GeoPoint End);
    public record Response(Guid RequestId);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/passenger/routes/{routeId}/requests", async (
            Guid routeId,
            Request req,
            HttpContext http,
            CreateRideRequestHandler handler,
            CancellationToken ct) =>
        {
            var passengerId = HttpUtils.GetPassengerId(http);
            var result      = await handler.HandleAsync(new(passengerId, routeId, req.Start, req.End), ct);
            return Results.Created($"/passenger/requests/{result.RequestId}", new Response(result.RequestId));
        });
}
