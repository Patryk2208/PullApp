using TripPlanner.Application.Repositories;

namespace TripPlanner.Api.Endpoints.Driver;

// GET /driver/requests — pending ride requests across all of the caller's (driver's) routes.
// Survives reload (the dashboard otherwise only sees live ride_requested SSE events).
// 200 OK [RideRequestDto]
// 401 Unauthorized – missing X-User-Id header
public class GetDriverRequestsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/driver/requests", async (
            HttpContext http,
            IRideRequestRepository requests,
            CancellationToken ct) =>
        {
            var driverId = HttpUtils.GetDriverId(http);
            var list = await requests.GetPendingByDriverIdAsync(driverId, ct);
            return Results.Ok(list.Select(RideRequestDto.From));
        });
}
