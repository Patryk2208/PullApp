using System.Text.Json;
using TripPlanner.Application.Exceptions;
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
    IKafkaPublisher kafka,
    ISseHub sseHub)
{
    public async Task HandleAsync(DriverConfirmationCommand cmd, CancellationToken ct)
    {
        var request = await rideRequests.GetByIdAsync(cmd.RequestId, ct)
            ?? throw new NotFoundException("no_active_request");

        if (request.Status != Domain.Passenger.RideRequestStatus.PendingDriver)
            throw new InvalidStateTransitionException("invalid_state_transition");

        var selectedEntry = request.MatchResults?
            .FirstOrDefault(m => m.DriverId == cmd.DriverId)
            ?? throw new NotFoundException("not_found");

        if (!cmd.Accepted)
        {
            var isEmpty = request.RemoveDriverFromResults(selectedEntry.DriverRouteId);
            if (isEmpty) request.ReSearch(Guid.NewGuid()); // TODO: re-dispatch
            else request.PresentMatches(request.MatchResults!);

            await rideRequests.UpdateAsync(request, ct);

            await kafka.PublishAsync<MatchDeclinedEvent>(Topics.UserActions,
                new MatchDeclinedEvent(cmd.RequestId, cmd.DriverId, request.PassengerId), ct);

            var remaining = request.MatchResults?.Count ?? 0;
            await sseHub.PushAsync(cmd.RequestId, "match_declined",
                JsonSerializer.Serialize(new MatchDeclinedEvent(cmd.RequestId, cmd.DriverId, request.PassengerId)), ct);

            if (remaining > 0)
                await sseHub.PushAsync(cmd.RequestId, "routes_ready",
                    JsonSerializer.Serialize(new { request_id = cmd.RequestId, matches = request.MatchResults }), ct);
            return;
        }

        // Accepted — create the ride.
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

        // Chat room (spec: 3s timeout, retry once).
        var chatRoomId = await chat.CreateRoomAsync(ride.Id, cmd.DriverId, request.PassengerId, ct);
        ride.SetChatRoom(chatRoomId);

        // Price freeze (spec: 5s timeout, retry once).
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

        await kafka.PublishAsync<MatchConfirmedEvent>(Topics.UserActions,
            new MatchConfirmedEvent(ride.Id, cmd.DriverId, request.PassengerId), ct);

        await sseHub.PushAsync(cmd.RequestId, "match_confirmed",
            JsonSerializer.Serialize(new MatchConfirmedSseEvent(
                cmd.RequestId,
                ride.Id,
                chatRoomId,
                "Driver", // TODO: fetch display name from Accounts
                0,        // TODO: fetch rating from Accounts
                selectedEntry.EtaToPassengerSeconds,
                quote.Amount,
                quote.Currency)),
            ct);
    }
}
