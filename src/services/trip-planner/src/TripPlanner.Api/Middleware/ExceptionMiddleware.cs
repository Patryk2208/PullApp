using System.Text.Json;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Features.DTO;

namespace TripPlanner.Api.Middleware;

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
            BadRequestException e                => (400, e.Message, e.Message),
            NotFoundException e                  => (404, e.Message, e.Message),
            ForbiddenException e                 => (403, e.Message, e.Message),
            RouteAlreadyActiveException e        => (409, e.Message, e.Message),
            CannotModifyDuringRideException e    => (409, e.Message, e.Message),
            InvalidStateTransitionException e    => (409, e.Message, e.Message),
            RequestExpiredException e            => (409, e.Message, e.Message),
            DriverUnavailableException e         => (409, e.Message, e.Message),
            OutsideServiceAreaException e        => (422, e.Message, e.Message),
            AccountsUnavailableException e       => (503, e.Message, e.Message),
            PaymentsUnavailableException e       => (503, e.Message, e.Message),
            ChatUnavailableException e           => (503, e.Message, e.Message),
            UnauthorizedAccessException          => (401, "unauthorized", ex.Message),
            _                                    => (500, "internal_error", "An unexpected error occurred"),
        };

        if (status == 500)
            logger.LogError(ex, "Unhandled exception");

        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";

        var body = JsonSerializer.Serialize(new ErrorResponseDto(code, message, null));
        await ctx.Response.WriteAsync(body);
    }
}
