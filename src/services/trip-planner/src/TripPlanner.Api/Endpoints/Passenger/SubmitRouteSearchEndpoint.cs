using TripPlanner.Application.Features.Passenger;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Api.Endpoints.Passenger;

// POST /passenger/routes/search
// Flow 2: passenger submits a route search.
// Matching is computed asynchronously by route-calc via RabbitMQ.
// Results are delivered via RouteSearchCompletedEvent → notifications service SSE/push.
// The returned jobId can be used by the client to correlate the incoming push notification.
//
// 202 Accepted        – { jobId } – search queued; await SSE/push for results
// 401 Unauthorized    – missing X-User-Id header
// 422 Unprocessable   – start or end point outside active service area
// 503 Service Unavail – route-calc unavailable
public class SubmitRouteSearchEndpoint : IEndpoint
{
    public record Request(GeoPoint Start, GeoPoint End, long DepartureDate, int SeatsNeeded, int MaxDetourKm = 10, int TimeWindowMinutes = 120);
    public record Response(Guid JobId);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/passenger/routes/search", async (
            Request req,
            HttpContext http,
            SubmitRouteSearchHandler handler,
            CancellationToken ct) =>
        {
            var passengerId = HttpUtils.GetPassengerId(http);
            var result      = await handler.HandleAsync(
                new(passengerId, req.Start, req.End, req.DepartureDate, req.SeatsNeeded, req.MaxDetourKm, req.TimeWindowMinutes), ct);
            return Results.Accepted((string?)null, new Response(result.JobId));
        });
}
