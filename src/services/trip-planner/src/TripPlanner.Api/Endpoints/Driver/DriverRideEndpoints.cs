using TripPlanner.Application.Features.DTO;
using TripPlanner.Application.Features.DTO.Driver;
using TripPlanner.Application.Features.Driver;

namespace TripPlanner.Api.Endpoints.Driver;

// POST /api/driver/rides/{rideId}/confirmation — accept or decline match
// POST /api/driver/rides/{rideId}/arrived      — driver arrived at pickup
// POST /api/driver/rides/{rideId}/start        — start the ride
// POST /api/driver/rides/{rideId}/complete     — complete the ride
// POST /api/driver/rides/{rideId}/cancel       — cancel the ride

public class DriverConfirmationEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("api/driver/requests/{requestId}/confirmation", Handle)
           .WithName("DriverConfirmation");

    private static async Task<IResult> Handle(
        Guid requestId,
        DriverConfirmationRequest req,
        ConfirmationHandler handler,
        HttpContext http,
        CancellationToken ct)
    {
        var driverId = HttpUtils.GetDriverId(http);
        await handler.HandleAsync(new DriverConfirmationCommand(driverId, requestId, req.Accepted), ct);
        return Results.NoContent();
    }
}

public class DriverArrivedEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("api/driver/rides/{rideId}/arrived", Handle)
           .WithName("DriverArrived");

    private static async Task<IResult> Handle(
        Guid rideId,
        DriverArrivedHandler handler,
        HttpContext http,
        CancellationToken ct)
    {
        var driverId = HttpUtils.GetDriverId(http);
        await handler.HandleAsync(new DriverArrivedCommand(driverId, rideId), ct);
        return Results.NoContent();
    }
}

public class DriverStartRideEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("api/driver/rides/{rideId}/start", Handle)
           .WithName("DriverStartRide");

    private static async Task<IResult> Handle(
        Guid rideId,
        DriverStartRideHandler handler,
        HttpContext http,
        CancellationToken ct)
    {
        var driverId = HttpUtils.GetDriverId(http);
        await handler.HandleAsync(new DriverStartRideCommand(driverId, rideId), ct);
        return Results.NoContent();
    }
}

public class CompleteRideEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("api/driver/rides/{rideId}/complete", Handle)
           .WithName("CompleteRide");

    private static async Task<IResult> Handle(
        Guid rideId,
        CompleteRideRequest req,
        CompleteRideHandler handler,
        HttpContext http,
        CancellationToken ct)
    {
        var driverId = HttpUtils.GetDriverId(http);
        await handler.HandleAsync(new CompleteRideCommand(driverId, rideId, req.DropoffPoint), ct);
        return Results.NoContent();
    }
}

public class DriverCancelRideEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("api/driver/rides/{rideId}/cancel", Handle)
           .WithName("DriverCancelRide");

    private static async Task<IResult> Handle(
        Guid rideId,
        CancelRideRequest req,
        DriverCancelRideHandler handler,
        HttpContext http,
        CancellationToken ct)
    {
        var driverId = HttpUtils.GetDriverId(http);
        await handler.HandleAsync(new DriverCancelRideCommand(driverId, rideId, req.Reason), ct);
        return Results.NoContent();
    }
}
