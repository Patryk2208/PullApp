using System.Text.Json;
using Microsoft.Extensions.Logging;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Metrics;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Driver;
using TripPlanner.Application.Features.DTO;
using TripPlanner.Application.Features.DTO.Driver;

namespace TripPlanner.Application.Features.Driver;

public record ModifyRouteCommand(Guid DriverId, GeoPointDto Start, GeoPointDto End);

public class ModifyRouteHandler(
    IAccountsService accounts,
    IGeoService geo,
    IDriverRouteRepository driverRoutes,
    IRideRequestRepository rideRequests,
    IRouteJobRepository jobs,
    IComputePublisher<ComputeJob> queue,
    ISseHub sseHub,
    TripPlannerMetrics metrics,
    ILogger<ModifyRouteHandler> logger)
{
    public async Task<RegisterRouteResponse> HandleAsync(ModifyRouteCommand cmd, CancellationToken ct)
    {
        logger.LogDebug("ModifyRoute: driverId={DriverId}", cmd.DriverId);

        var existing = await driverRoutes.GetActiveByDriverIdAsync(cmd.DriverId, ct)
            ?? throw new NotFoundException("no_active_route");

        var activeRide = await rideRequests.GetActiveByPassengerIdAsync(cmd.DriverId, ct);
        if (activeRide is not null)
        {
            logger.LogWarning("ModifyRoute: driverId={DriverId} has active ride, cannot modify", cmd.DriverId);
            throw new CannotModifyDuringRideException();
        }

        var start = new GeoPoint(cmd.Start.Lat, cmd.Start.Lng);
        var end   = new GeoPoint(cmd.End.Lat,   cmd.End.Lng);

        if (!await geo.IsWithinServiceAreaAsync(start, ct) || !await geo.IsWithinServiceAreaAsync(end, ct))
        {
            logger.LogWarning("ModifyRoute: points outside service area for driverId={DriverId}", cmd.DriverId);
            throw new OutsideServiceAreaException();
        }

        existing.Cancel();
        await driverRoutes.UpdateAsync(existing, ct);
        logger.LogDebug("ModifyRoute: cancelled old route={RouteId}", existing.Id);

        var affectedRequestIds = await driverRoutes.GetPendingRequestIdsForRouteAsync(existing.Id, ct);
        logger.LogDebug("ModifyRoute: invalidating {Count} affected passenger requests", affectedRequestIds.Count());
        foreach (var requestId in affectedRequestIds)
        {
            var request = await rideRequests.GetByIdAsync(requestId, ct);
            if (request is null) continue;
            var isEmpty = request.RemoveDriverFromResults(existing.Id);
            if (isEmpty) request.ReSearch(Guid.NewGuid());
            await rideRequests.UpdateAsync(request, ct);
            await sseHub.PushAsync(requestId, isEmpty ? "routes_expired" : "routes_ready",
                System.Text.Json.JsonSerializer.Serialize(new { requestId }), ct);
        }

        var correlationId = Guid.NewGuid();
        var payload = new DriverRouteJobPayload(start, end);
        var computeJob = new DriverRouteComputeJob(correlationId, cmd.DriverId, payload, DateTimeOffset.UtcNow);
        var computeJobJson = JsonSerializer.Serialize(computeJob);

        var job = new RouteJob
        {
            Id            = Guid.NewGuid(),
            CorrelationId = correlationId,
            JobType       = JobType.DriverRoute,
            RequesterId   = cmd.DriverId,
            PayloadJson   = computeJobJson,
            CreatedAt     = DateTimeOffset.UtcNow,
        };

        var route = new DriverRoute
        {
            Id         = Guid.NewGuid(),
            DriverId   = cmd.DriverId,
            StartPoint = start,
            EndPoint   = end,
            JobId      = job.Id,
            CreatedAt  = DateTimeOffset.UtcNow,
        };

        await jobs.AddAsync(job, ct);
        await driverRoutes.AddAsync(route, ct);
        await queue.PublishAsync(computeJob, ct);

        metrics.RouteModified();
        logger.LogInformation("Driver {DriverId} modified route, new jobId={JobId}", cmd.DriverId, job.Id);

        return new RegisterRouteResponse(job.Id);
    }
}
