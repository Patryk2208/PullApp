using System.Text.Json;
using Microsoft.Extensions.Logging;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Metrics;
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
    IEventPublisher @event,
    ISseHub sseHub,
    TripPlannerMetrics metrics,
    ILogger<DriverArrivedHandler> logger)
{
    public async Task HandleAsync(DriverArrivedCommand cmd, CancellationToken ct)
    {
        logger.LogDebug("DriverArrived: driverId={DriverId} rideId={RideId}", cmd.DriverId, cmd.RideId);

        var ride = await rides.GetByIdAsync(cmd.RideId, ct)
            ?? throw new NotFoundException("not_found");

        if (ride.DriverId != cmd.DriverId) throw new ForbiddenException("forbidden");
        if (ride.Status != RideStatus.Pickup)
        {
            logger.LogWarning("DriverArrived: invalid state {Status} rideId={RideId}", ride.Status, cmd.RideId);
            throw new InvalidStateTransitionException("invalid_state_transition");
        }

        ride.MarkDriverArrived();
        await rides.UpdateAsync(ride, ct);

        await @event.PublishAsync(Topics.UserActions,
            new DriverArrivedEvent(ride.Id, ride.DriverId, ride.PassengerId), ct);

        await sseHub.PushAsync(ride.PassengerId, "driver_arrived",
            JsonSerializer.Serialize(new DriverArrivedSseEvent(ride.Id)), ct);

        metrics.RideTransition("pickup", "awaiting_passenger", "driver_arrived");
        logger.LogInformation("Driver {DriverId} arrived at pickup for rideId={RideId}", cmd.DriverId, cmd.RideId);
    }
}

// ─── Start Ride (driver side) ─────────────────────────────────────────────────

public record DriverStartRideCommand(Guid DriverId, Guid RideId);

public class DriverStartRideHandler(
    IRideRepository rides,
    IEventPublisher @event,
    ISseHub sseHub,
    TripPlannerMetrics metrics,
    ILogger<DriverStartRideHandler> logger)
{
    public async Task HandleAsync(DriverStartRideCommand cmd, CancellationToken ct)
    {
        logger.LogDebug("DriverStartRide: driverId={DriverId} rideId={RideId}", cmd.DriverId, cmd.RideId);

        var ride = await rides.GetByIdAsync(cmd.RideId, ct)
            ?? throw new NotFoundException("not_found");

        if (ride.DriverId != cmd.DriverId) throw new ForbiddenException("forbidden");
        if (ride.Status != RideStatus.AwaitingPassenger)
        {
            logger.LogWarning("DriverStartRide: invalid state {Status} rideId={RideId}", ride.Status, cmd.RideId);
            throw new InvalidStateTransitionException("invalid_state_transition");
        }

        ride.Start();
        await rides.UpdateAsync(ride, ct);

        await @event.PublishAsync<RideStartedEvent>(Topics.UserActions,
            new RideStartedEvent(ride.Id, ride.DriverId, ride.PassengerId, ride.StartedAt!.Value), ct);

        await sseHub.PushAsync(ride.PassengerId, "ride_started",
            JsonSerializer.Serialize(new RideStartedEvent(ride.Id, ride.DriverId, ride.PassengerId, ride.StartedAt.Value)), ct);

        metrics.RideTransition("awaiting_passenger", "in_ride", "driver_started");
        logger.LogInformation("Driver {DriverId} started rideId={RideId}", cmd.DriverId, cmd.RideId);
    }
}

// ─── Complete Ride ────────────────────────────────────────────────────────────

public record CompleteRideCommand(Guid DriverId, Guid RideId, GeoPointDto DropoffPoint);

public class CompleteRideHandler(
    IRideRepository rides,
    IDriverRouteRepository driverRoutes,
    IEventPublisher @event,
    ISseHub sseHub,
    TripPlannerMetrics metrics,
    ILogger<CompleteRideHandler> logger)
{
    public async Task HandleAsync(CompleteRideCommand cmd, CancellationToken ct)
    {
        logger.LogDebug("CompleteRide: driverId={DriverId} rideId={RideId}", cmd.DriverId, cmd.RideId);

        var ride = await rides.GetByIdAsync(cmd.RideId, ct)
            ?? throw new NotFoundException("not_found");

        if (ride.DriverId != cmd.DriverId) throw new ForbiddenException("forbidden");
        if (ride.Status != RideStatus.InRide)
        {
            logger.LogWarning("CompleteRide: invalid state {Status} rideId={RideId}", ride.Status, cmd.RideId);
            throw new InvalidStateTransitionException("invalid_state_transition");
        }

        var dropoff = new GeoPoint(cmd.DropoffPoint.Lat, cmd.DropoffPoint.Lng);
        ride.Complete(dropoff);
        await rides.UpdateAsync(ride, ct);
        logger.LogDebug("CompleteRide: ride completed, duration={Duration}s rideId={RideId}",
            (int)(ride.CompletedAt!.Value - ride.StartedAt!.Value).TotalSeconds, cmd.RideId);

        var driverRoute = await driverRoutes.GetByIdAsync(ride.DriverRouteId, ct);

        await @event.PublishAsync(Topics.RideCompletions,
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

        metrics.RideCompleted();
        metrics.RideTransition("in_ride", "completed", "normal");
        metrics.RideActiveAdd(-1);
        logger.LogInformation("Ride {RideId} completed by driver {DriverId}, amount={Amount}",
            cmd.RideId, cmd.DriverId, ride.FrozenPriceAmount);
    }
}

// ─── Cancel Ride (driver side) ────────────────────────────────────────────────

public record DriverCancelRideCommand(Guid DriverId, Guid RideId, string? Reason);

public class DriverCancelRideHandler(
    IRideRepository rides,
    IRideRequestRepository rideRequests,
    IEventPublisher @event,
    ISseHub sseHub,
    TripPlannerMetrics metrics,
    ILogger<DriverCancelRideHandler> logger)
{
    public async Task HandleAsync(DriverCancelRideCommand cmd, CancellationToken ct)
    {
        logger.LogDebug("DriverCancelRide: driverId={DriverId} rideId={RideId} reason={Reason}",
            cmd.DriverId, cmd.RideId, cmd.Reason);

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

        await @event.PublishAsync(Topics.RideCompletions,
            new RideCancelledEvent(
                ride.Id, ride.DriverId, ride.PassengerId,
                ride.FrozenPriceId,
                "driver",
                phase.ToString().ToLowerInvariant(),
                ride.CancelledAt!.Value),
            ct);

        await sseHub.PushAsync(ride.PassengerId, "ride_cancelled",
            JsonSerializer.Serialize(new RideCancelledSseEvent(ride.Id, "driver", cmd.Reason)), ct);

        metrics.RideCancelled("driver");
        metrics.RideTransition(phase == CancellationPhase.InRide ? "in_ride" : "pre_pickup", "cancelled", "driver_cancel");
        metrics.RideActiveAdd(-1);
        logger.LogInformation("Driver {DriverId} cancelled rideId={RideId} phase={Phase} reason={Reason}",
            cmd.DriverId, cmd.RideId, phase, cmd.Reason);
    }
}
