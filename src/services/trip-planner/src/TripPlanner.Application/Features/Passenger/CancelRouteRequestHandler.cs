using System.Text.Json;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Passenger;
using TripPlanner.Application.Features.DTO.Sse;

namespace TripPlanner.Application.Features.Passenger;

public record CancelRouteRequestCommand(Guid PassengerId, Guid RequestId);

public class CancelRouteRequestHandler(
    IRideRequestRepository rideRequests,
    ISseHub sseHub)
{
    public async Task HandleAsync(CancelRouteRequestCommand cmd, CancellationToken ct)
    {
        var request = await rideRequests.GetByIdAsync(cmd.RequestId, ct)
            ?? throw new NotFoundException("no_active_request");

        if (request.PassengerId != cmd.PassengerId) throw new ForbiddenException("forbidden");

        if (request.Status == RideRequestStatus.MatchConfirmed)
            throw new InvalidStateTransitionException("already_confirmed_use_ride_cancel");

        request.Cancel();
        await rideRequests.UpdateAsync(request, ct);

        // If a compute job is still in flight, ResultsQueueConsumer will detect the
        // cancelled status and discard the result (spec §12).

        await sseHub.PushAsync(cmd.RequestId, "cancelled",
            JsonSerializer.Serialize(new RequestCancelledEvent(cmd.RequestId)), ct);

        await sseHub.CloseAsync(cmd.RequestId, ct);
    }
}
