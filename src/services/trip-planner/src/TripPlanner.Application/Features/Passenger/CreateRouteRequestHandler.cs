using System.Text.Json;
using Microsoft.Extensions.Logging;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Metrics;
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
    IComputePublisher<ComputeJob> queue,
    TripPlannerMetrics metrics,
    ILogger<CreateRouteRequestHandler> logger)
{
    public async Task<PassengerRouteRequestResponse> HandleAsync(
        CreateRouteRequestCommand cmd, CancellationToken ct)
    {
        logger.LogDebug("CreateRouteRequest: passengerId={PassengerId} start={Start} end={End} maxDetour={MaxDetour}",
            cmd.PassengerId, cmd.Start, cmd.End, cmd.MaxDetourKm);

        if (!await accounts.IsPassengerActiveAsync(cmd.PassengerId, ct))
        {
            logger.LogWarning("CreateRouteRequest: accounts unavailable for passengerId={PassengerId}", cmd.PassengerId);
            metrics.MatchingRequestRecorded("failed_validation");
            throw new AccountsUnavailableException();
        }

        var start = new GeoPoint(cmd.Start.Lat, cmd.Start.Lng);
        var end   = new GeoPoint(cmd.End.Lat,   cmd.End.Lng);

        if (!await geo.IsWithinServiceAreaAsync(start, ct) || !await geo.IsWithinServiceAreaAsync(end, ct))
        {
            logger.LogWarning("CreateRouteRequest: points outside service area for passengerId={PassengerId}", cmd.PassengerId);
            metrics.MatchingRequestRecorded("no_area_coverage");
            throw new OutsideServiceAreaException();
        }

        if (await rideRequests.GetActiveByPassengerIdAsync(cmd.PassengerId, ct) is not null)
        {
            logger.LogWarning("CreateRouteRequest: active request already exists for passengerId={PassengerId}", cmd.PassengerId);
            metrics.MatchingRequestRecorded("failed_validation");
            throw new InvalidStateTransitionException("active_request_exists");
        }

        var correlationId = Guid.NewGuid();
        var constraints   = new MatchConstraints(cmd.MaxDetourKm, cmd.MaxResults);

        var payload = new PassengerMatchJobPayload(start, end, constraints);
        var computeJob = new PassengerMatchComputeJob(correlationId, cmd.PassengerId, payload, DateTimeOffset.UtcNow);
        var computeJobJson = JsonSerializer.Serialize(computeJob);

        var job = new RouteJob
        {
            Id            = Guid.NewGuid(),
            CorrelationId = correlationId,
            JobType       = JobType.PassengerMatch,
            RequesterId   = cmd.PassengerId,
            PayloadJson   = computeJobJson,
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
        logger.LogDebug("CreateRouteRequest: persisted requestId={RequestId} jobId={JobId}", request.Id, job.Id);

        await queue.PublishAsync(computeJob, ct);
        logger.LogDebug("CreateRouteRequest: published compute job correlationId={CorrelationId}", correlationId);

        metrics.MatchingRequestRecorded("queued");
        metrics.RecordMatchingJobPublished(correlationId);
        metrics.RouteRequestCreated();
        logger.LogInformation("Passenger {PassengerId} created route request {RequestId}", cmd.PassengerId, request.Id);

        return new PassengerRouteRequestResponse(request.Id);
    }
}
