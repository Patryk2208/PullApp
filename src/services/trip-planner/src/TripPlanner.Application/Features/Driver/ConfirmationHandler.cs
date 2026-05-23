using System.Text.Json;
using Microsoft.Extensions.Logging;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Metrics;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Events;
using TripPlanner.Domain.Ride;
using TripPlanner.Application.Features.DTO.Sse;
using MatchDeclinedEvent = TripPlanner.Domain.Events.MatchDeclinedEvent;

namespace TripPlanner.Application.Features.Driver;

public record DriverConfirmationCommand(Guid DriverId, Guid RequestId, bool Accepted);

public class ConfirmationHandler(
    IRideRequestRepository rideRequests,
    IDriverRouteRepository driverRoutes,
    IRideRepository rides,
    IChatService chat,
    IPaymentsService payments,
    IEventPublisher @event,
    ISseHub sseHub,
    TripPlannerMetrics metrics,
    ILogger<ConfirmationHandler> logger)
{
    public async Task HandleAsync(DriverConfirmationCommand cmd, CancellationToken ct)
    {
        logger.LogDebug("DriverConfirmation: driverId={DriverId} requestId={RequestId} accepted={Accepted}",
            cmd.DriverId, cmd.RequestId, cmd.Accepted);

        var request = await rideRequests.GetByIdAsync(cmd.RequestId, ct)
            ?? throw new NotFoundException("no_active_request");

        if (request.Status != Domain.Passenger.RideRequestStatus.PendingDriver)
        {
            logger.LogWarning("DriverConfirmation: invalid state {Status} for requestId={RequestId}",
                request.Status, cmd.RequestId);
            throw new InvalidStateTransitionException("invalid_state_transition");
        }

        var selectedEntry = request.MatchResults?
            .FirstOrDefault(m => m.DriverId == cmd.DriverId)
            ?? throw new NotFoundException("not_found");

        if (!cmd.Accepted)
        {
            logger.LogInformation("Driver {DriverId} declined match for requestId={RequestId}", cmd.DriverId, cmd.RequestId);
            metrics.MatchDeclined();
            metrics.DriverDeclined("explicit");
            metrics.RecordAcceptanceEnded(cmd.RequestId);
            metrics.RideTransition("pending_driver", "searching", "driver_declined");

            var isEmpty = request.RemoveDriverFromResults(selectedEntry.DriverRouteId);
            if (isEmpty) request.ReSearch(Guid.NewGuid()); // TODO: re-dispatch
            else request.PresentMatches(request.MatchResults!);

            await rideRequests.UpdateAsync(request, ct);

            await @event.PublishAsync<MatchDeclinedEvent>(Topics.UserActions,
                new MatchDeclinedEvent(cmd.RequestId, cmd.DriverId, request.PassengerId), ct);

            var remaining = request.MatchResults?.Count ?? 0;
            logger.LogDebug("DriverConfirmation: {Remaining} matches remaining for requestId={RequestId}", remaining, cmd.RequestId);
            await sseHub.PushAsync(cmd.RequestId, "match_declined",
                JsonSerializer.Serialize(new MatchDeclinedEvent(cmd.RequestId, cmd.DriverId, request.PassengerId)), ct);

            if (remaining > 0)
                await sseHub.PushAsync(cmd.RequestId, "routes_ready",
                    JsonSerializer.Serialize(new { request_id = cmd.RequestId, matches = request.MatchResults }), ct);
            return;
        }

        // Accepted — create the ride.
        logger.LogDebug("DriverConfirmation: accepted, creating ride for requestId={RequestId}", cmd.RequestId);

        var driverRoute = await driverRoutes.GetByIdAsync(selectedEntry.DriverRouteId, ct)
            ?? throw new DriverUnavailableException();

        request.Confirm();
        await rideRequests.UpdateAsync(request, ct);

        var ride = new Ride
        {
            Id            = Guid.NewGuid(),
            RequestId     = cmd.RequestId,
            DriverId      = cmd.DriverId,
            PassengerId   = request.PassengerId,
            DriverRouteId = selectedEntry.DriverRouteId,
            PickupPoint   = request.StartPoint,
            CreatedAt     = DateTimeOffset.UtcNow,
        };

        logger.LogDebug("DriverConfirmation: creating chat room for rideId={RideId}", ride.Id);
        var chatRoomId = await chat.CreateRoomAsync(ride.Id, cmd.DriverId, request.PassengerId, ct);
        ride.SetChatRoom(chatRoomId);

        logger.LogDebug("DriverConfirmation: fetching price quote for rideId={RideId}", ride.Id);
        var quote = await payments.QuotePriceAsync(
            selectedEntry.DriverRouteId,
            request.PassengerId,
            request.StartPoint,
            request.EndPoint,
            driverRoute.DistanceMeters ?? 0,
            selectedEntry.EtaToPassengerSeconds,
            ct);
        ride.FreezePrice(quote.FrozenPriceId, quote.Amount, quote.ExpiresAt);

        await rides.AddAsync(ride, ct);

        await @event.PublishAsync<MatchConfirmedEvent>(Topics.UserActions,
            new MatchConfirmedEvent(ride.Id, cmd.DriverId, request.PassengerId), ct);

        await sseHub.PushAsync(ride.PassengerId, "match_confirmed",
            JsonSerializer.Serialize(new MatchConfirmedSseEvent(
                cmd.RequestId,
                ride.Id,
                chatRoomId,
                "Robert Kubica", // TODO: fetch display name from Accounts
                5,        // TODO: fetch rating from Accounts
                selectedEntry.EtaToPassengerSeconds,
                quote.Amount,
                quote.Currency)),
            ct);

        metrics.MatchConfirmed();
        metrics.RecordAcceptanceEnded(cmd.RequestId);
        metrics.RideTransition("pending_driver", "pickup", "driver_accepted");
        metrics.RideActiveAdd(1);
        logger.LogInformation("Driver {DriverId} confirmed match, rideId={RideId} passengerId={PassengerId}",
            cmd.DriverId, ride.Id, ride.PassengerId);
    }
}
