using TripPlanner.Application.Features.DTO.Passenger;
using TripPlanner.Application.Features.Passenger;
using TripPlanner.Infrastructure.Sse;

namespace TripPlanner.Api.Endpoints.Passenger;

// POST   /api/passenger/route-requests            — create a route request
// GET    /api/passenger/route-requests/{id}/events — SSE stream
// POST   /api/passenger/route-requests/{id}/select  — select a driver route
// DELETE /api/passenger/route-requests/{id}        — cancel request

public class CreateRouteRequestEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("api/passenger/route-requests", Handle)
           .WithName("CreateRouteRequest");

    private static async Task<IResult> Handle(
        PassengerRouteRequest req,
        CreateRouteRequestHandler handler,
        HttpContext http,
        CancellationToken ct)
    {
        var passengerId = HttpUtils.GetPassengerId(http);
        var c = req.Constraints;
        var response = await handler.HandleAsync(
            new CreateRouteRequestCommand(
                passengerId,
                req.Start,
                req.End,
                c?.MaxDetourKm ?? 5,
                c?.MaxResults ?? 5),
            ct);
        return Results.Accepted($"/api/passenger/route-requests/{response.RequestId}/events", response);
    }
}

public class SseStreamEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("api/passenger/route-requests/{requestId}/events", Handle)
           .WithName("SseStream");

    private static async Task Handle(
        Guid requestId,
        InMemorySseHub hub,
        HttpContext http,
        CancellationToken ct)
    {
        http.Response.Headers.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers.Connection = "keep-alive";

        var channel = hub.Register(requestId);

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
            hub.Unregister(requestId);
        }
    }
}

public class SelectRouteEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("api/passenger/route-requests/{requestId}/select", Handle)
           .WithName("SelectRoute");

    private static async Task<IResult> Handle(
        Guid requestId,
        SelectRouteRequest req,
        SelectRouteHandler handler,
        HttpContext http,
        CancellationToken ct)
    {
        var passengerId = HttpUtils.GetPassengerId(http);
        await handler.HandleAsync(new SelectRouteCommand(passengerId, requestId, req.DriverRouteId), ct);
        return Results.NoContent();
    }
}

public class CancelRouteRequestEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("api/passenger/route-requests/{requestId}", Handle)
           .WithName("CancelRouteRequest");

    private static async Task<IResult> Handle(
        Guid requestId,
        CancelRouteRequestHandler handler,
        HttpContext http,
        CancellationToken ct)
    {
        var passengerId = HttpUtils.GetPassengerId(http);
        await handler.HandleAsync(new CancelRouteRequestCommand(passengerId, requestId), ct);
        return Results.NoContent();
    }
}
