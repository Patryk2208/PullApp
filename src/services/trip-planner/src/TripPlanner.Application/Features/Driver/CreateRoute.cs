using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Application.Features.Driver;

public record CreateRouteCommand(Guid DriverId, GeoPoint Start, GeoPoint End, int Capacity);
public record CreateRouteResult(Guid RouteId);

/// <summary>
/// Flow 0 — driver creates a Route (async geometry computation).
/// </summary>
public class CreateRouteHandler(
    IRouteRepository routes,
    IRouteJobRepository jobs,
    IComputePublisher<ComputeJob> computePublisher,
    IGeoService geo,
    IAccountsService accounts,
    IUnitOfWork uow)
{
    public async Task<CreateRouteResult> HandleAsync(CreateRouteCommand cmd, CancellationToken ct)
    {
        // Flow 0
        // 1. Verify the driver has driving privileges (IAccountsService.CanDriveAsync).
        // 2. Validate Start and End are within the active service area (IGeoService.IsWithinServiceAreaAsync).
        // 3. Create Route aggregate with Status = Calculating; persist it.
        // 4. Build a RouteJob (DriverRoute type) for audit / reply correlation; persist it.
        // 5. Publish DriverRouteComputeJob to RabbitMQ using RouteJob.CorrelationId.
        // 6. Commit the transaction.
        // 7. Return the routeId.
        //    When route-calc responds, RouteComputedHandler sets geometry and publishes
        //    RouteReadyEvent → notifications service delivers SSE/push to the driver.
        throw new NotImplementedException();
    }
}
