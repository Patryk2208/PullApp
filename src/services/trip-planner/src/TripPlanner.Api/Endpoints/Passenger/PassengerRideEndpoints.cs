using TripPlanner.Application.Features.DTO.Driver;
using TripPlanner.Application.Features.Passenger;

namespace TripPlanner.Api.Endpoints.Passenger;

// POST /api/passenger/rides/{rideId}/start         — passenger confirms start
// POST /api/passenger/rides/{rideId}/confirm-price — acknowledge updated fare
// POST /api/passenger/rides/{rideId}/cancel        — cancel an active ride

public class PassengerStartRideEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("api/passenger/rides/{rideId}/start", Handle)
           .WithName("PassengerStartRide");

    private static async Task<IResult> Handle(
        Guid rideId,
        PassengerStartRideHandler handler,
        HttpContext http,
        CancellationToken ct)
    {
        var passengerId = HttpUtils.GetPassengerId(http);
        await handler.HandleAsync(new PassengerStartRideCommand(passengerId, rideId), ct);
        return Results.NoContent();
    }
}

public class ConfirmPriceEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("api/passenger/rides/{rideId}/confirm-price", Handle)
           .WithName("ConfirmPrice");

    private static async Task<IResult> Handle(
        Guid rideId,
        ConfirmPriceHandler handler,
        HttpContext http,
        CancellationToken ct)
    {
        var passengerId = HttpUtils.GetPassengerId(http);
        await handler.HandleAsync(new ConfirmPriceCommand(passengerId, rideId), ct);
        return Results.NoContent();
    }
}

public class PassengerCancelRideEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("api/passenger/rides/{rideId}/cancel", Handle)
           .WithName("PassengerCancelRide");

    private static async Task<IResult> Handle(
        Guid rideId,
        CancelRideRequest req,
        PassengerCancelRideHandler handler,
        HttpContext http,
        CancellationToken ct)
    {
        var passengerId = HttpUtils.GetPassengerId(http);
        await handler.HandleAsync(new PassengerCancelRideCommand(passengerId, rideId, req.Reason), ct);
        return Results.NoContent();
    }
}
