using TripPlanner.Application.Features.DTO.Driver;
using TripPlanner.Application.Features.Driver;

namespace TripPlanner.Api.Endpoints.Driver;

// POST /api/driver/route        — register a new route
// GET  /api/driver/route/{jobId} — poll compute job status
// PUT  /api/driver/route        — modify existing route
// DELETE /api/driver/route      — cancel route

public class RegisterRouteEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("api/driver/route", Handle)
           .WithName("RegisterRoute");

    private static async Task<IResult> Handle(
        RegisterRouteRequest req,
        RegisterRouteHandler handler,
        HttpContext http,
        CancellationToken ct)
    {
        var driverId = HttpUtils.GetDriverId(http);
        var response = await handler.HandleAsync(new RegisterRouteCommand(driverId, req.Start, req.End), ct);
        return Results.Accepted($"/api/driver/route/{response.JobId}", response);
    }
}

public class GetRouteStatusEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("api/driver/route/{jobId}", Handle)
           .WithName("GetRouteStatus");

    private static async Task<IResult> Handle(
        Guid jobId,
        GetRouteStatusHandler handler,
        HttpContext http,
        CancellationToken ct)
    {
        var driverId = HttpUtils.GetDriverId(http);
        var response = await handler.HandleAsync(new GetRouteStatusQuery(jobId, driverId), ct);
        return Results.Ok(response);
    }
}

public class ModifyRouteEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("api/driver/route", Handle)
           .WithName("ModifyRoute");

    private static async Task<IResult> Handle(
        ModifyRouteRequest req,
        ModifyRouteHandler handler,
        HttpContext http,
        CancellationToken ct)
    {
        var driverId = HttpUtils.GetDriverId(http);
        var response = await handler.HandleAsync(
            new ModifyRouteCommand(driverId, req.Start, req.End), ct);
        return Results.Accepted($"/api/driver/route/{response.JobId}", response);
    }
}

public class CancelRouteEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("api/driver/route", Handle)
           .WithName("CancelRoute");

    private static async Task<IResult> Handle(
        CancelRouteHandler handler,
        HttpContext http,
        CancellationToken ct)
    {
        var driverId = HttpUtils.GetDriverId(http);
        await handler.HandleAsync(new CancelRouteCommand(driverId), ct);
        return Results.NoContent();
    }
}
