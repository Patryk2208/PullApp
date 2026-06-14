using TripPlanner.Application.Repositories;

namespace TripPlanner.Api.Endpoints.Passenger;

// GET /passenger/requests — all ride requests the caller (passenger) has made, any status.
// 200 OK [RideRequestDto]
// 401 Unauthorized – missing X-User-Id header
public class GetPassengerRequestsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/passenger/requests", async (
            HttpContext http,
            IRideRequestRepository requests,
            CancellationToken ct) =>
        {
            var passengerId = HttpUtils.GetPassengerId(http);
            var list = await requests.GetByPassengerIdAsync(passengerId, ct);
            return Results.Ok(list.Select(RideRequestDto.From));
        });
}
