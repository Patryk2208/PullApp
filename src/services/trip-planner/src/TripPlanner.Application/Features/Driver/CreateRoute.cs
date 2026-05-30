using System.Text.Json;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Metrics;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Route;

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
    TripPlannerMetrics metrics,
    IUnitOfWork uow)
{
    public async Task<CreateRouteResult> HandleAsync(CreateRouteCommand cmd, CancellationToken ct)
    {
        // Flow 0
        // 1. Verify the driver has driving privileges (IAccountsService.CanDriveAsync).
        if (!await accounts.CanDriveAsync(cmd.DriverId, ct))
            throw new UnauthorizedException("Driver is not authorised to create routes.");

        // 2. Validate Start and End are within the active service area (IGeoService.IsWithinServiceAreaAsync).
        if (!await geo.IsWithinServiceAreaAsync(cmd.Start, ct) || !await geo.IsWithinServiceAreaAsync(cmd.End, ct))
            throw new OutsideServiceAreaException("Route start or end is outside the active service area.");

        // 3. Create Route aggregate with Status = Calculating; persist it.
        var route = Route.Create(cmd.DriverId, cmd.Start, cmd.End, cmd.Capacity);
        await routes.AddAsync(route, ct);

        // 4. Build a RouteJob (DriverRoute type) for audit / reply correlation; persist it.
        var correlationId = Guid.NewGuid();
        var payload = new DriverRouteJobPayload(cmd.Start, cmd.End);
        var job = new RouteJob
        {
            Id            = Guid.NewGuid(),
            CorrelationId = correlationId,
            JobType       = JobType.DriverRoute,
            RequesterId   = cmd.DriverId,
            PayloadJson   = JsonSerializer.Serialize(payload),
            CreatedAt     = DateTimeOffset.UtcNow,
        };
        await jobs.AddAsync(job, ct);

        // 5. Publish DriverRouteComputeJob to RabbitMQ using RouteJob.CorrelationId.
        // 6. Commit the transaction.
        // Commit first so the route and job exist before route-calc sends a reply.
        await uow.CommitAsync(ct);

        await computePublisher.PublishAsync(
            new DriverRouteComputeJob(correlationId, cmd.DriverId, payload, DateTimeOffset.UtcNow), ct);

        metrics.DriverRouteRegistrationQueued();
        metrics.RecordRouteCalcPublished(correlationId, "route_registration");

        // 7. Return the routeId.
        //    When route-calc responds, RouteComputedHandler sets geometry and publishes
        //    RouteReadyEvent → notifications service delivers SSE/push to the driver.
        return new CreateRouteResult(route.Id);
    }
}
