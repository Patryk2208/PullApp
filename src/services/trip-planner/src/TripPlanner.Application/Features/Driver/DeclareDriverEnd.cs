using Microsoft.Extensions.Logging;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Metrics;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Events;
using TripPlanner.Domain.Ride;

namespace TripPlanner.Application.Features.Driver;

public record DeclareDriverEndCommand(Guid DriverId, Guid RideId);

/// <summary>
/// Flow 8c (driver side) — driver declares the end of a Started ride.
/// Driver declaration is ignored unless the passenger has already declared end.
/// If both parties declared, the ride is completed and payment is charged.
/// </summary>
public class DeclareDriverEndHandler(
    IRideRepository rides,
    IRouteRepository routes,
    IRideRequestRepository rideRequests,
    IPaymentsService payments,
    IEventPublisher events,
    TripPlannerMetrics metrics,
    IUnitOfWork uow,
    ILogger<DeclareDriverEndHandler> logger)
{
    public async Task HandleAsync(DeclareDriverEndCommand cmd, CancellationToken ct)
    {
        // Flow 8c — driver side
        // 1. Load Ride; verify it belongs to the driver.
        var ride = await rides.GetByIdAsync(cmd.RideId, ct)
            ?? throw new RideNotFoundException(cmd.RideId);

        if (ride.DriverId != cmd.DriverId)
            throw new UnauthorizedException($"Ride {cmd.RideId} belongs to a different driver.");

        if (ride.Status != RideStatus.Started)
            throw new InvalidRouteStatusException(
                $"Ride must be in Started status to declare end (current: {ride.Status}).");

        // 2. Call ride.DeclareDriverEnd().
        //    - Returns false if passenger hasn't declared yet → throw DeclarationOrderException (403).
        if (!ride.DeclareDriverEnd())
            throw new DeclarationOrderException("Passenger has not declared end of ride yet.");

        // 3. ride.IsEnded is true (both declared):

        // 3a. Load Route; call route.RemoveRide() → Status may revert Active from Full.
        var route = await routes.GetByIdAsync(ride.RouteId, ct);
        if (route is not null)
        {
            route.RemoveRide();
            await routes.UpdateAsync(route, ct);
        }

        // 3b. Charge the passenger (IPaymentsService.ChargeAsync on FrozenPriceId).
        await payments.ChargeAsync(ride.FrozenPriceId!.Value, ct);

        // 3c. Persist ride and commit.
        await rides.UpdateAsync(ride, ct);
        await uow.CommitAsync(ct);

        // 3d. Load all previously Rejected RideRequests for this route.
        var rejectedRequests = await rideRequests.GetRejectedByRouteIdAsync(ride.RouteId, ct);
        var notifyPassengerIds = rejectedRequests.Select(r => r.PassengerId).ToList();

        // 3e. Publish RideEndedEvent (notifies rejected passengers that a seat may be free).
        await events.PublishAsync(Topics.NotificationTriggers,
            new RideEndedEvent(ride.Id, ride.RouteId, ride.DriverId, ride.PassengerId, notifyPassengerIds), ct);

        // 3f. Publish RideCompletedEvent (billing confirmation to payments service).
        await events.PublishAsync(Topics.RideCompletions,
            new RideCompletedEvent(ride.Id, ride.DriverId, ride.PassengerId,
                ride.FrozenPriceId!.Value, ride.Price, ride.EndedAt!.Value), ct);

        metrics.RideTransition("started", "completed", "normal");
        metrics.RideActiveAdd(-1);
        logger.LogInformation("Ride completed rideId={RideId} driverId={DriverId} passengerId={PassengerId}",
            ride.Id, cmd.DriverId, ride.PassengerId);
    }
}
