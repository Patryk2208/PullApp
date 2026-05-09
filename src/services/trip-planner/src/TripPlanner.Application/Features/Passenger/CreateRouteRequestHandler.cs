using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Passenger;
using TripPlanner.Application.Features.DTO;
using TripPlanner.Application.Features.DTO.Passenger;

namespace TripPlanner.Application.Features.Passenger;

public record CreateRouteRequestCommand(
    Guid PassengerId,
    GeoPointDto Start,
    GeoPointDto End,
    double MaxDetourKm = 5,
    int MaxResults = 5);

public class CreateRouteRequestHandler(
    IAccountsService accounts,
    IGeoService geo,
    IRideRequestRepository rideRequests,
    IRouteJobRepository jobs,
    IRouteCalculator calculator)
{
    public async Task<PassengerRouteRequestResponse> HandleAsync(
        CreateRouteRequestCommand cmd, CancellationToken ct)
    {
        if (!await accounts.IsPassengerActiveAsync(cmd.PassengerId, ct))
            throw new AccountsUnavailableException();

        var start = new GeoPoint(cmd.Start.Lat, cmd.Start.Lng);
        var end   = new GeoPoint(cmd.End.Lat,   cmd.End.Lng);

        if (!await geo.IsWithinServiceAreaAsync(start, ct) ||
            !await geo.IsWithinServiceAreaAsync(end, ct))
            throw new OutsideServiceAreaException();

        if (await rideRequests.GetActiveByPassengerIdAsync(cmd.PassengerId, ct) is not null)
            throw new InvalidStateTransitionException("active_request_exists");

        var correlationId = Guid.NewGuid();
        var constraints   = new MatchConstraints(cmd.MaxDetourKm, cmd.MaxResults);

        var job = new RouteJob
        {
            Id            = Guid.NewGuid(),
            CorrelationId = correlationId,
            JobType       = JobType.PassengerMatch,
            RequesterId   = cmd.PassengerId,
            PayloadJson   = "{}",
            ExpiresAt     = DateTimeOffset.UtcNow.AddMinutes(10),
            CreatedAt     = DateTimeOffset.UtcNow,
        };

        var request = new RideRequest
        {
            Id          = Guid.NewGuid(),
            PassengerId = cmd.PassengerId,
            StartPoint  = start,
            EndPoint    = end,
            Constraints = constraints,
            CreatedAt   = DateTimeOffset.UtcNow,
            UpdatedAt   = DateTimeOffset.UtcNow,
        };
        request.AssignJob(job.Id);

        await jobs.AddAsync(job, ct);
        await rideRequests.AddAsync(request, ct);

        await calculator.SendComputeAsync(
            new PassengerMatchComputeJob(
                correlationId,
                cmd.PassengerId,
                new PassengerMatchJobPayload(start, end, constraints),
                DateTimeOffset.UtcNow),
            ct);

        return new PassengerRouteRequestResponse(request.Id);
    }
}
