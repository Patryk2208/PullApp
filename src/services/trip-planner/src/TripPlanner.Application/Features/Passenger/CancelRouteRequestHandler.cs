using System.Text.Json;
using Microsoft.Extensions.Logging;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Metrics;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Passenger;
using TripPlanner.Application.Features.DTO.Sse;

namespace TripPlanner.Application.Features.Passenger;

public record CancelRouteRequestCommand(Guid PassengerId, Guid RequestId);

public class CancelRouteRequestHandler(
    IRideRequestRepository rideRequests,
    ISseHub sseHub,
    TripPlannerMetrics metrics,
    ILogger<CancelRouteRequestHandler> logger)
{
    public async Task HandleAsync(CancelRouteRequestCommand cmd, CancellationToken ct)
    {
        logger.LogDebug("CancelRouteRequest: passengerId={PassengerId} requestId={RequestId}",
            cmd.PassengerId, cmd.RequestId);

        var request = await rideRequests.GetByIdAsync(cmd.RequestId, ct)
            ?? throw new NotFoundException("no_active_request");

        if (request.PassengerId != cmd.PassengerId) throw new ForbiddenException("forbidden");

        if (request.Status == RideRequestStatus.MatchConfirmed)
        {
            logger.LogWarning("CancelRouteRequest: already confirmed, use ride cancel. requestId={RequestId}", cmd.RequestId);
            throw new InvalidStateTransitionException("already_confirmed_use_ride_cancel");
        }

        request.Cancel();
        await rideRequests.UpdateAsync(request, ct);

        await sseHub.PushAsync(cmd.RequestId, "cancelled",
            JsonSerializer.Serialize(new RequestCancelledEvent(cmd.RequestId)), ct);
        await sseHub.CloseAsync(cmd.RequestId, ct);

        metrics.RouteRequestCancelled();
        logger.LogInformation("Passenger {PassengerId} cancelled route request {RequestId}", cmd.PassengerId, cmd.RequestId);
    }
}
