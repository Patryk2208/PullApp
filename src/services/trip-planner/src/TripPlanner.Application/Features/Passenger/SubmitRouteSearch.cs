using System.Text.Json;
using Microsoft.Extensions.Logging;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Metrics;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Application.Features.Passenger;

public record SubmitRouteSearchCommand(
    Guid PassengerId,
    GeoPoint Start,
    GeoPoint End,
    long DepartureDate,
    int SeatsNeeded,
    int MaxDetourKm = 10,
    int TimeWindowMinutes = 120);
public record SubmitRouteSearchResult(Guid JobId);

/// <summary>
/// Flow 2 (submit) — passenger submits a route search.
/// Returns a job ID for client-side correlation; results are pushed asynchronously.
/// </summary>
public class SubmitRouteSearchHandler(
    IRouteJobRepository jobs,
    IComputePublisher<ComputeJob> computePublisher,
    IGeoService geo,
    TripPlannerMetrics metrics,
    IUnitOfWork uow,
    ILogger<SubmitRouteSearchHandler> logger)
{
    public async Task<SubmitRouteSearchResult> HandleAsync(SubmitRouteSearchCommand cmd, CancellationToken ct)
    {
        // Flow 2 — submit
        // 1. Validate Start and End are within the active service area (IGeoService.IsWithinServiceAreaAsync).
        if (!await geo.IsWithinServiceAreaAsync(cmd.Start, ct) || !await geo.IsWithinServiceAreaAsync(cmd.End, ct))
        {
            metrics.MatchingRequestRecorded("no_area_coverage");
            throw new OutsideServiceAreaException("Search start or end is outside the active service area.");
        }

        // 2. Create a RouteJob (RideMatching type) with a new CorrelationId; persist it.
        var correlationId = Guid.NewGuid();
        var payload = new RideMatchingJobPayload(
            cmd.Start, cmd.End, cmd.DepartureDate, cmd.SeatsNeeded, cmd.MaxDetourKm, cmd.TimeWindowMinutes);
        var job = new RouteJob
        {
            Id            = Guid.NewGuid(),
            CorrelationId = correlationId,
            JobType       = JobType.RideMatching,
            RequesterId   = cmd.PassengerId,
            PayloadJson   = JsonSerializer.Serialize(payload),
            CreatedAt     = DateTimeOffset.UtcNow,
            ExpiresAt     = DateTimeOffset.UtcNow.AddMinutes(10),
        };
        await jobs.AddAsync(job, ct);

        // 3. Publish RideMatchingComputeJob to RabbitMQ using the CorrelationId.
        // 4. Commit.
        // Commit before publishing so the job exists before a reply could arrive.
        await uow.CommitAsync(ct);

        await computePublisher.PublishAsync(
            new RideMatchingComputeJob(correlationId, cmd.PassengerId, payload, DateTimeOffset.UtcNow), ct);

        metrics.MatchingRequestRecorded("queued");
        metrics.RecordMatchingJobPublished(correlationId);
        metrics.RecordRouteCalcPublished(correlationId, "ride_matching");

        // 5. Return the RouteJob ID (for client-side correlation of the incoming push).
        //    When route-calc responds, RouteComputedHandler publishes RouteSearchCompletedEvent
        //    → notifications service delivers the match list via SSE/push to the passenger.
        logger.LogInformation("Route search submitted passengerId={PassengerId} jobId={JobId} correlationId={CorrelationId}",
            cmd.PassengerId, job.Id, correlationId);
        return new SubmitRouteSearchResult(job.Id);
    }
}
