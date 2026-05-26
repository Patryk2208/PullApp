using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
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
    IUnitOfWork uow)
{
    public async Task<SubmitRouteSearchResult> HandleAsync(SubmitRouteSearchCommand cmd, CancellationToken ct)
    {
        // Flow 2 — submit
        // 1. Validate Start and End are within the active service area (IGeoService.IsWithinServiceAreaAsync).
        // 2. Create a RouteJob (PassengerMatch type) with a new CorrelationId; persist it.
        // 3. Publish PassengerMatchComputeJob to RabbitMQ using the CorrelationId.
        // 4. Commit.
        // 5. Return the RouteJob ID (for client-side correlation of the incoming push).
        //    When route-calc responds, RouteComputedHandler publishes RouteSearchCompletedEvent
        //    → notifications service delivers the match list via SSE/push to the passenger.
        throw new NotImplementedException();
    }
}
