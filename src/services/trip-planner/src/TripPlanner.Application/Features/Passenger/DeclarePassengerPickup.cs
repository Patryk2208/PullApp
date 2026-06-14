using Microsoft.Extensions.Logging;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Events;
using TripPlanner.Domain.Ride;

namespace TripPlanner.Application.Features.Passenger;

public record DeclarePassengerPickupCommand(Guid PassengerId, Guid RideId);

/// <summary>
/// Flow 7 (passenger side) — passenger confirms they have been picked up by the driver.
/// Driver must declare pickup first; out-of-order declaration returns 403.
/// Both declarations are required before Status transitions to Started.
/// </summary>
public class DeclarePassengerPickupHandler(
    IRideRepository rides,
    IEventPublisher events,
    KafkaTopics topics,
    IUnitOfWork uow,
    ILogger<DeclarePassengerPickupHandler> logger)
{
    public async Task HandleAsync(DeclarePassengerPickupCommand cmd, CancellationToken ct)
    {
        // Flow 7 — passenger side
        // 1. Load Ride; verify it belongs to the passenger.
        var ride = await rides.GetByIdAsync(cmd.RideId, ct)
            ?? throw new RideNotFoundException(cmd.RideId);

        if (ride.PassengerId != cmd.PassengerId)
            throw new UnauthorizedException($"Ride {cmd.RideId} belongs to a different passenger.");

        if (ride.Status != RideStatus.WaitingForDriver)
            throw new InvalidRouteStatusException(
                $"Ride must be in WaitingForDriver status to declare pickup (current: {ride.Status}).");

        // 2. Call ride.DeclarePassengerPickup().
        //    - Returns false if driver hasn't declared yet → throw DeclarationOrderException (403).
        //    - Returns true and Status becomes Started when both parties have declared.
        var wasStartedBefore = ride.Status == RideStatus.Started;
        if (!ride.DeclarePassengerPickup())
            throw new DeclarationOrderException("Driver has not declared pickup yet.");
        var isStartedNow = ride.Status == RideStatus.Started;

        // 3. Persist and commit.
        await rides.UpdateAsync(ride, ct);
        await uow.CommitAsync(ct);

        // 4. Publish events.
        await events.PublishAsync(topics.NotificationTriggers,
            new PassengerDeclaredPickupEvent(ride.Id, ride.RouteId, ride.DriverId, ride.PassengerId), ct);

        if (!wasStartedBefore && isStartedNow)
        {
            await events.PublishAsync(topics.NotificationTriggers,
                new RideStartedEvent(ride.Id, ride.RouteId, ride.DriverId, ride.PassengerId), ct);
        }

        logger.LogInformation("Passenger declared pickup rideId={RideId} passengerId={PassengerId} rideStarted={Started}",
            cmd.RideId, cmd.PassengerId, isStartedNow);
    }
}
