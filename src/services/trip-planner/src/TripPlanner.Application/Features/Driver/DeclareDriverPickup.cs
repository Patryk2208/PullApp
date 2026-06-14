using Microsoft.Extensions.Logging;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Events;
using TripPlanner.Domain.Ride;

namespace TripPlanner.Application.Features.Driver;

public record DeclareDriverPickupCommand(Guid DriverId, Guid RideId);

/// <summary>
/// Flow 7 (driver side) — driver declares that they have picked up the passenger.
/// Only after both driver and passenger declare pickup does the Ride start.
/// </summary>
public class DeclareDriverPickupHandler(
    IRideRepository rides,
    IEventPublisher events,
    KafkaTopics topics,
    IUnitOfWork uow,
    ILogger<DeclareDriverPickupHandler> logger)
{
    public async Task HandleAsync(DeclareDriverPickupCommand cmd, CancellationToken ct)
    {
        // Flow 7 — driver side
        // 1. Load Ride; verify it belongs to the driver and Status == WaitingForDriver.
        var ride = await rides.GetByIdAsync(cmd.RideId, ct)
            ?? throw new RideNotFoundException(cmd.RideId);

        if (ride.DriverId != cmd.DriverId)
            throw new UnauthorizedException($"Ride {cmd.RideId} belongs to a different driver.");

        if (ride.Status != RideStatus.WaitingForDriver)
            throw new InvalidRouteStatusException(
                $"Ride must be in WaitingForDriver status to declare pickup (current: {ride.Status}).");

        // 2. Call ride.DeclareDriverPickup().
        //    - If both parties have now declared → Status becomes Started (handled inside domain).
        var wasStartedBefore = ride.Status == RideStatus.Started;
        ride.DeclareDriverPickup();
        var isStartedNow = ride.Status == RideStatus.Started;

        // 3. Persist and commit.
        await rides.UpdateAsync(ride, ct);
        await uow.CommitAsync(ct);

        // 4. Publish events.
        await events.PublishAsync(topics.NotificationTriggers,
            new DriverDeclaredPickupEvent(ride.Id, ride.RouteId, ride.DriverId, ride.PassengerId), ct);

        if (!wasStartedBefore && isStartedNow)
        {
            await events.PublishAsync(topics.NotificationTriggers,
                new RideStartedEvent(ride.Id, ride.RouteId, ride.DriverId, ride.PassengerId), ct);
        }

        logger.LogInformation("Driver declared pickup rideId={RideId} driverId={DriverId} rideStarted={RideStarted}",
            cmd.RideId, cmd.DriverId, isStartedNow);
    }
}
