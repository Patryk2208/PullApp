using TripPlanner.Application.Features.Driver;
using TripPlanner.Application.Features.DTO;
using TripPlanner.Application.Features.DTO.Driver;
using TripPlanner.Infrastructure.Sse;

namespace TripPlanner.Api.Endpoints.Driver;

// POST /api/driver/route        — register a new route
// GET  /api/driver/route/events — SSE stream
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
        if (req.Start is null || req.End is null)
        {
            return Results.BadRequest(
                new ErrorResponseDto(
                    "invalid_request",
                    "start and end are required",
                    null));
        }

        var driverId = HttpUtils.GetDriverId(http);
        var response = await handler.HandleAsync(new RegisterRouteCommand(driverId, req.Start, req.End), ct);
        return Results.Accepted($"/api/driver/route/{response.JobId}", response);
    }
}

public class GetRouteStatusEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("api/driver/route/{jobId}/events", Handle)
           .WithName("ReadyRouteSseStream");

    private static async Task Handle(
        Guid jobId,
        InMemorySseHub hub,
        HttpContext http,
        CancellationToken ct)
    {
        http.Response.Headers.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers.Connection = "keep-alive";
        
        var driverId = HttpUtils.GetDriverId(http);
        var channel = hub.Register(driverId);

        try
        {
            await foreach (var msg in channel.Reader.ReadAllAsync(ct))
            {
                await http.Response.WriteAsync($"event: {msg.EventType}\n", ct);
                await http.Response.WriteAsync($"data: {msg.Json}\n\n", ct);
                await http.Response.Body.FlushAsync(ct);

                if (msg.Close) break;
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        finally
        {
            hub.Unregister(driverId);
        }
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
        var response = await handler.HandleAsync(new ModifyRouteCommand(driverId, req.Start, req.End), ct);
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
