using Microsoft.Extensions.Logging;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Ride;

namespace TripPlanner.Application.Features.Passenger;

public record DeclarePassengerEndCommand(Guid PassengerId, Guid RideId);

/// <summary>
/// Flow 8c (passenger side) — passenger declares the ride is over.
/// Passenger MUST declare first; the driver's declaration alone is a 403.
/// Full completion (payment + events) happens when the driver also calls their /end endpoint.
/// </summary>
public class DeclarePassengerEndHandler(
    IRideRepository rides,
    IUnitOfWork uow,
    ILogger<DeclarePassengerEndHandler> logger)
{
    public async Task HandleAsync(DeclarePassengerEndCommand cmd, CancellationToken ct)
    {
        // Flow 8c — passenger side
        // 1. Load Ride; verify it belongs to the passenger and Status == Started.
        var ride = await rides.GetByIdAsync(cmd.RideId, ct)
            ?? throw new RideNotFoundException(cmd.RideId);

        if (ride.PassengerId != cmd.PassengerId)
            throw new UnauthorizedException($"Ride {cmd.RideId} belongs to a different passenger.");

        if (ride.Status != RideStatus.Started)
            throw new InvalidRouteStatusException(
                $"Ride must be in Started status to declare end (current: {ride.Status}).");

        // 2. Call ride.DeclarePassengerEnd() → records the declaration.
        //    Completion happens only when driver also declares (DeclareDriverEnd).
        ride.DeclarePassengerEnd();

        // 3. Persist and commit.
        await rides.UpdateAsync(ride, ct);
        await uow.CommitAsync(ct);
        logger.LogInformation("Passenger declared end rideId={RideId} passengerId={PassengerId} awaitingDriver=true",
            cmd.RideId, cmd.PassengerId);
    }
}
