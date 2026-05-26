using System.Text.Json;
using TripPlanner.Application.Exceptions;

namespace TripPlanner.Api.Middleware;

public record ErrorResponse(string Code, string Message);

public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (Exception ex)
        {
            await HandleAsync(ctx, ex, logger);
        }
    }

    private static async Task HandleAsync(HttpContext ctx, Exception ex, ILogger logger)
    {
        var (status, code, message) = ex switch
        {
            RouteNotFoundException e         => (404, "route_not_found", e.Message),
            RideNotFoundException e          => (404, "ride_not_found", e.Message),
            RideRequestNotFoundException e   => (404, "ride_request_not_found", e.Message),
            RouteFullException e             => (409, "route_full", e.Message),
            RouteNotDeletableException e     => (409, "route_not_deletable", e.Message),
            InvalidRouteStatusException e    => (409, "invalid_status", e.Message),
            OutsideServiceAreaException e    => (422, "outside_service_area", e.Message),
            UnauthorizedException e          => (403, "forbidden", e.Message),
            DeclarationOrderException e      => (403, "declaration_order", e.Message),
            DownstreamUnavailableException e => (503, "downstream_unavailable", e.Message),
            UnauthorizedAccessException      => (401, "unauthorized", ex.Message),
            _                                => (500, "internal_error", "An unexpected error occurred"),
        };

        switch (status)
        {
            case 500:
                logger.LogError(ex, "Unhandled exception on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
                break;
            case 503:
                logger.LogWarning("Downstream unavailable on {Method} {Path}: {Message}",
                    ctx.Request.Method, ctx.Request.Path, ex.Message);
                break;
            case 409 or 422:
                logger.LogWarning("Business rule violation ({Code}) on {Method} {Path}: {Message}",
                    code, ctx.Request.Method, ctx.Request.Path, ex.Message);
                break;
            case 404:
                logger.LogDebug("Not found ({Code}) on {Method} {Path}", code, ctx.Request.Method, ctx.Request.Path);
                break;
            case 401 or 403:
                logger.LogWarning("Auth failure ({Status}) on {Method} {Path}", status, ctx.Request.Method, ctx.Request.Path);
                break;
        }

        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new ErrorResponse(code, message)));
    }
}
