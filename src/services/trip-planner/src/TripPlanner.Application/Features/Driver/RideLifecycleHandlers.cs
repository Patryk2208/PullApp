using System.Text.Json;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Events;
using TripPlanner.Domain.Ride;
using TripPlanner.Application.Features.DTO;
using TripPlanner.Application.Features.DTO.Sse;
using RideStartedEvent = TripPlanner.Domain.Events.RideStartedEvent;

namespace TripPlanner.Application.Features.Driver;

// ─── Driver Arrived ───────────────────────────────────────────────────────────

public record DriverArrivedCommand(Guid DriverId, Guid RideId);

public class DriverArrivedHandler(
    IRideRepository rides,
    IKafkaPublisher kafka,
    ISseHub sseHub)
{
    public async Task HandleAsync(DriverArrivedCommand cmd, CancellationToken ct)
    {
        var ride = await rides.GetByIdAsync(cmd.RideId, ct)
            ?? throw new NotFoundException("not_found");

        if (ride.DriverId != cmd.DriverId) throw new ForbiddenException("forbidden");
        if (ride.Status != RideStatus.Pickup) throw new InvalidStateTransitionException("invalid_state_transition");

        ride.MarkDriverArrived();
        await rides.UpdateAsync(ride, ct);

        await kafka.PublishAsync(Topics.UserActions,
            new DriverArrivedEvent(ride.Id, ride.DriverId, ride.PassengerId), ct);

        await sseHub.PushAsync(ride.PassengerId, "driver_arrived",
            JsonSerializer.Serialize(new DriverArrivedSseEvent(ride.Id)), ct);
    }
}

// ─── Start Ride (driver side) ─────────────────────────────────────────────────

public record DriverStartRideCommand(Guid DriverId, Guid RideId);

public class DriverStartRideHandler(
    IRideRepository rides,
    IKafkaPublisher kafka,
    ISseHub sseHub)
{
    public async Task HandleAsync(DriverStartRideCommand cmd, CancellationToken ct)
    {
        var ride = await rides.GetByIdAsync(cmd.RideId, ct)
            ?? throw new NotFoundException("not_found");

        if (ride.DriverId != cmd.DriverId) throw new ForbiddenException("forbidden");
        if (ride.Status != RideStatus.AwaitingPassenger) throw new InvalidStateTransitionException("invalid_state_transition");

        ride.Start();
        await rides.UpdateAsync(ride, ct);

        await kafka.PublishAsync<RideStartedEvent>(Topics.UserActions,
            new RideStartedEvent(ride.Id, ride.DriverId, ride.PassengerId, ride.StartedAt!.Value), ct);

        await sseHub.PushAsync(ride.PassengerId, "ride_started",
            JsonSerializer.Serialize(new RideStartedEvent(ride.Id, ride.DriverId, ride.PassengerId, ride.StartedAt.Value)), ct);
    }
}

// ─── Complete Ride ────────────────────────────────────────────────────────────

public record CompleteRideCommand(Guid DriverId, Guid RideId, GeoPointDto DropoffPoint);

public class CompleteRideHandler(
    IRideRepository rides,
    IDriverRouteRepository driverRoutes,
    IKafkaPublisher kafka,
    ISseHub sseHub)
{
    public async Task HandleAsync(CompleteRideCommand cmd, CancellationToken ct)
    {
        var ride = await rides.GetByIdAsync(cmd.RideId, ct)
            ?? throw new NotFoundException("not_found");

        if (ride.DriverId != cmd.DriverId) throw new ForbiddenException("forbidden");
        if (ride.Status != RideStatus.InRide) throw new InvalidStateTransitionException("invalid_state_transition");

        var dropoff = new GeoPoint(cmd.DropoffPoint.Lat, cmd.DropoffPoint.Lng);
        ride.Complete(dropoff);
        await rides.UpdateAsync(ride, ct);

        var driverRoute = await driverRoutes.GetByIdAsync(ride.DriverRouteId, ct);
        // Driver's route stays active per spec §21 #9.

        await kafka.PublishAsync(Topics.RideCompletions,
            new RideCompletedEvent(
                ride.Id,
                ride.DriverId,
                ride.PassengerId,
                ride.FrozenPriceId!.Value,
                ride.FrozenPriceAmount!.Value,
                driverRoute?.DistanceMeters ?? 0,
                (int)(ride.CompletedAt!.Value - ride.StartedAt!.Value).TotalSeconds,
                ride.CompletedAt.Value),
            ct);

        await sseHub.PushAsync(ride.PassengerId, "ride_completed",
            JsonSerializer.Serialize(new RideCompletedSseEvent(
                ride.Id,
                ride.FrozenPriceAmount!.Value,
                "PLN",
                "Driver")), // TODO: fetch display name
            ct);
    }
}

// ─── Cancel Ride (driver side) ────────────────────────────────────────────────

public record DriverCancelRideCommand(Guid DriverId, Guid RideId, string? Reason);

public class DriverCancelRideHandler(
    IRideRepository rides,
    IRideRequestRepository rideRequests,
    IKafkaPublisher kafka,
    ISseHub sseHub)
{
    public async Task HandleAsync(DriverCancelRideCommand cmd, CancellationToken ct)
    {
        var ride = await rides.GetByIdAsync(cmd.RideId, ct)
            ?? throw new NotFoundException("not_found");

        if (ride.DriverId != cmd.DriverId) throw new ForbiddenException("forbidden");

        var phase = ride.Status switch
        {
            RideStatus.Pickup or RideStatus.AwaitingPassenger => CancellationPhase.PrePickup,
            RideStatus.InRide => CancellationPhase.InRide,
            _ => throw new InvalidStateTransitionException("invalid_state_transition"),
        };

        ride.Cancel(CancelledBy.Driver);
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
                "driver",
                phase.ToString().ToLowerInvariant(),
                ride.CancelledAt!.Value),
            ct);

        await sseHub.PushAsync(ride.PassengerId, "ride_cancelled",
            JsonSerializer.Serialize(new RideCancelledSseEvent(ride.Id, "driver", cmd.Reason)), ct);
    }
}
