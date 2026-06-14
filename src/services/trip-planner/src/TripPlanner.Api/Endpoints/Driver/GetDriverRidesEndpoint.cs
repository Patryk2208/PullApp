using TripPlanner.Application.Repositories;

namespace TripPlanner.Api.Endpoints.Driver;

// GET /driver/rides — all rides on the caller's (driver's) routes, newest first.
// 200 OK [RideDto]
// 401 Unauthorized – missing X-User-Id header
public class GetDriverRidesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/driver/rides", async (
            HttpContext http,
            IRideRepository rides,
            CancellationToken ct) =>
        {
            var driverId = HttpUtils.GetDriverId(http);
            var list = await rides.GetByDriverIdAsync(driverId, ct);
            return Results.Ok(list.Select(RideDto.From));
        });
}
