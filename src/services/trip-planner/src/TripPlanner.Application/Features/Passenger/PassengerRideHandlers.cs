using System.Text.Json;
using Microsoft.Extensions.Logging;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Metrics;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Events;
using TripPlanner.Domain.Ride;
using TripPlanner.Application.Features.DTO.Sse;
using RideStartedEvent = TripPlanner.Domain.Events.RideStartedEvent;

namespace TripPlanner.Application.Features.Passenger;

// ─── Start Ride (passenger side) ─────────────────────────────────────────────

public record PassengerStartRideCommand(Guid PassengerId, Guid RideId);

public class PassengerStartRideHandler(
    IRideRepository rides,
    IEventPublisher @event,
    ISseHub sseHub,
    TripPlannerMetrics metrics,
    ILogger<PassengerStartRideHandler> logger)
{
    public async Task HandleAsync(PassengerStartRideCommand cmd, CancellationToken ct)
    {
        logger.LogDebug("PassengerStartRide: passengerId={PassengerId} rideId={RideId}", cmd.PassengerId, cmd.RideId);

        var ride = await rides.GetByIdAsync(cmd.RideId, ct)
            ?? throw new NotFoundException("not_found");

        if (ride.PassengerId != cmd.PassengerId) throw new ForbiddenException("forbidden");
        if (ride.Status != RideStatus.AwaitingPassenger)
        {
            logger.LogWarning("PassengerStartRide: invalid state {Status} rideId={RideId}", ride.Status, cmd.RideId);
            throw new InvalidStateTransitionException("invalid_state_transition");
        }

        ride.Start();
        await rides.UpdateAsync(ride, ct);

        await @event.PublishAsync<RideStartedEvent>(Topics.UserActions,
            new RideStartedEvent(ride.Id, ride.DriverId, ride.PassengerId, ride.StartedAt!.Value), ct);

        await sseHub.PushAsync(ride.PassengerId, "ride_started",
            JsonSerializer.Serialize(new RideStartedEvent(ride.Id, ride.DriverId, ride.PassengerId, ride.StartedAt.Value)), ct);

        metrics.RideTransition(ride.Id, "awaiting_passenger", "in_ride", "passenger_started");
        logger.LogInformation("Passenger {PassengerId} confirmed ride start, rideId={RideId}", cmd.PassengerId, cmd.RideId);
    }
}

// ─── Confirm Price (after price freeze expiry) ────────────────────────────────

public record ConfirmPriceCommand(Guid PassengerId, Guid RideId);

public class ConfirmPriceHandler(
    IRideRepository rides,
    ILogger<ConfirmPriceHandler> logger)
{
    public async Task HandleAsync(ConfirmPriceCommand cmd, CancellationToken ct)
    {
        logger.LogDebug("ConfirmPrice: passengerId={PassengerId} rideId={RideId}", cmd.PassengerId, cmd.RideId);

        var ride = await rides.GetByIdAsync(cmd.RideId, ct)
            ?? throw new NotFoundException("not_found");

        if (ride.PassengerId != cmd.PassengerId) throw new ForbiddenException("forbidden");

        if (ride.Status is not (RideStatus.Pickup or RideStatus.AwaitingPassenger))
        {
            logger.LogWarning("ConfirmPrice: invalid state {Status} rideId={RideId}", ride.Status, cmd.RideId);
            throw new InvalidStateTransitionException("invalid_state_transition");
        }

        logger.LogInformation("Passenger {PassengerId} confirmed updated price for rideId={RideId}", cmd.PassengerId, cmd.RideId);
        // TODO: persist a "price_confirmed_at" timestamp on the ride record.
    }
}

// ─── Cancel Ride (passenger side) ────────────────────────────────────────────

public record PassengerCancelRideCommand(Guid PassengerId, Guid RideId, string? Reason);

public class PassengerCancelRideHandler(
    IRideRepository rides,
    IRideRequestRepository rideRequests,
    IEventPublisher @event,
    ISseHub sseHub,
    TripPlannerMetrics metrics,
    ILogger<PassengerCancelRideHandler> logger)
{
    public async Task HandleAsync(PassengerCancelRideCommand cmd, CancellationToken ct)
    {
        logger.LogDebug("PassengerCancelRide: passengerId={PassengerId} rideId={RideId} reason={Reason}",
            cmd.PassengerId, cmd.RideId, cmd.Reason);

        var ride = await rides.GetByIdAsync(cmd.RideId, ct)
            ?? throw new NotFoundException("not_found");

        if (ride.PassengerId != cmd.PassengerId) throw new ForbiddenException("forbidden");

        var phase = ride.Status switch
        {
            RideStatus.Pickup or RideStatus.AwaitingPassenger => CancellationPhase.PrePickup,
            RideStatus.InRide => CancellationPhase.InRide,
            _ => throw new InvalidStateTransitionException("invalid_state_transition"),
        };

        ride.Cancel(CancelledBy.Passenger);
        await rides.UpdateAsync(ride, ct);

        var request = await rideRequests.GetByIdAsync(ride.RequestId, ct);
        if (request is not null)
        {
            request.Cancel();
            await rideRequests.UpdateAsync(request, ct);
        }

        await @event.PublishAsync(Topics.RideCompletions,
            new RideCancelledEvent(
                ride.Id, ride.DriverId, ride.PassengerId,
                ride.FrozenPriceId,
                "passenger",
                phase.ToString().ToLowerInvariant(),
                ride.CancelledAt!.Value),
            ct);

        await sseHub.PushAsync(ride.DriverId, "ride_cancelled",
            JsonSerializer.Serialize(new RideCancelledSseEvent(ride.Id, "passenger", cmd.Reason)), ct);

        metrics.RideCancelled("passenger", phase == CancellationPhase.InRide ? "during_ride" : "after_match");
        metrics.RideTransition(ride.Id, phase == CancellationPhase.InRide ? "in_ride" : "pre_pickup", "cancelled", "passenger_cancel");
        metrics.RideActiveAdd(-1);
        logger.LogInformation("Passenger {PassengerId} cancelled rideId={RideId} phase={Phase} reason={Reason}",
            cmd.PassengerId, cmd.RideId, phase, cmd.Reason);
    }
}
