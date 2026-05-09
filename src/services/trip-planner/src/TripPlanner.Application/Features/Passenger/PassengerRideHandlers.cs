using System.Text.Json;
using TripPlanner.Application.Exceptions;
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
    IKafkaPublisher kafka,
    ISseHub sseHub)
{
    public async Task HandleAsync(PassengerStartRideCommand cmd, CancellationToken ct)
    {
        var ride = await rides.GetByIdAsync(cmd.RideId, ct)
            ?? throw new NotFoundException("not_found");

        if (ride.PassengerId != cmd.PassengerId) throw new ForbiddenException("forbidden");
        if (ride.Status != RideStatus.AwaitingPassenger) throw new InvalidStateTransitionException("invalid_state_transition");

        ride.Start();
        await rides.UpdateAsync(ride, ct);

        await kafka.PublishAsync<RideStartedEvent>(Topics.UserActions,
            new RideStartedEvent(ride.Id, ride.DriverId, ride.PassengerId, ride.StartedAt!.Value), ct);

        await sseHub.PushAsync(ride.PassengerId, "ride_started",
            JsonSerializer.Serialize(new RideStartedEvent(ride.Id, ride.DriverId, ride.PassengerId, ride.StartedAt.Value)), ct);
    }
}

// ─── Confirm Price (after price freeze expiry) ────────────────────────────────

public record ConfirmPriceCommand(Guid PassengerId, Guid RideId);

public class ConfirmPriceHandler(IRideRepository rides)
{
    public async Task HandleAsync(ConfirmPriceCommand cmd, CancellationToken ct)
    {
        var ride = await rides.GetByIdAsync(cmd.RideId, ct)
            ?? throw new NotFoundException("not_found");

        if (ride.PassengerId != cmd.PassengerId) throw new ForbiddenException("forbidden");

        if (ride.Status is not (RideStatus.Pickup or RideStatus.AwaitingPassenger))
            throw new InvalidStateTransitionException("invalid_state_transition");

        // Price is already updated by PriceFreezeMonitorWorker before this endpoint is called.
        // This endpoint just records that the passenger acknowledged the new fare.
        // TODO: persist a "price_confirmed_at" timestamp on the ride record.
    }
}

// ─── Cancel Ride (passenger side) ────────────────────────────────────────────

public record PassengerCancelRideCommand(Guid PassengerId, Guid RideId, string? Reason);

public class PassengerCancelRideHandler(
    IRideRepository rides,
    IRideRequestRepository rideRequests,
    IKafkaPublisher kafka,
    ISseHub sseHub)
{
    public async Task HandleAsync(PassengerCancelRideCommand cmd, CancellationToken ct)
    {
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

        await kafka.PublishAsync(Topics.RideCompletions,
            new RideCancelledEvent(
                ride.Id, ride.DriverId, ride.PassengerId,
                ride.FrozenPriceId,
                "passenger",
                phase.ToString().ToLowerInvariant(),
                ride.CancelledAt!.Value),
            ct);

        await sseHub.PushAsync(ride.DriverId, "ride_cancelled",
            JsonSerializer.Serialize(new RideCancelledSseEvent(ride.Id, "passenger", cmd.Reason)), ct);
    }
}
