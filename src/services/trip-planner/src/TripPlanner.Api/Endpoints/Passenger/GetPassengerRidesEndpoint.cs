using TripPlanner.Application.Repositories;

namespace TripPlanner.Api.Endpoints.Passenger;

// GET /passenger/rides — all rides the caller (passenger) is part of, newest first.
// 200 OK [RideDto]
// 401 Unauthorized – missing X-User-Id header
public class GetPassengerRidesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/passenger/rides", async (
            HttpContext http,
            IRideRepository rides,
            CancellationToken ct) =>
        {
            var passengerId = HttpUtils.GetPassengerId(http);
            var list = await rides.GetByPassengerIdAsync(passengerId, ct);
            return Results.Ok(list.Select(RideDto.From));
        });
}
