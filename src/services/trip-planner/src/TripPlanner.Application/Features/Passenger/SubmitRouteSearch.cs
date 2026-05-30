using System.Text.Json;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Metrics;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Application.Features.Passenger;

public record SubmitRouteSearchCommand(Guid PassengerId, GeoPoint Start, GeoPoint End);
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
    IUnitOfWork uow)
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

        // 2. Create a RouteJob (PassengerMatch type) with a new CorrelationId; persist it.
        var correlationId = Guid.NewGuid();
        var payload = new PassengerMatchJobPayload(cmd.Start, cmd.End, new MatchConstraints());
        var job = new RouteJob
        {
            Id            = Guid.NewGuid(),
            CorrelationId = correlationId,
            JobType       = JobType.PassengerMatch,
            RequesterId   = cmd.PassengerId,
            PayloadJson   = JsonSerializer.Serialize(payload),
            CreatedAt     = DateTimeOffset.UtcNow,
            ExpiresAt     = DateTimeOffset.UtcNow.AddMinutes(10),
        };
        await jobs.AddAsync(job, ct);

        // 3. Publish PassengerMatchComputeJob to RabbitMQ using the CorrelationId.
        // 4. Commit.
        // Commit before publishing so the job exists before a reply could arrive.
        await uow.CommitAsync(ct);

        await computePublisher.PublishAsync(
            new PassengerMatchComputeJob(correlationId, cmd.PassengerId, payload, DateTimeOffset.UtcNow), ct);

        metrics.MatchingRequestRecorded("queued");
        metrics.RecordMatchingJobPublished(correlationId);
        metrics.RecordRouteCalcPublished(correlationId, "passenger_match");

        // 5. Return the RouteJob ID (for client-side correlation of the incoming push).
        //    When route-calc responds, RouteComputedHandler publishes RouteSearchCompletedEvent
        //    → notifications service delivers the match list via SSE/push to the passenger.
        return new SubmitRouteSearchResult(job.Id);
    }
}
