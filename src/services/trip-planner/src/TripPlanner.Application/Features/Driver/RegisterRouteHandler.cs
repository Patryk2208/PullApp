using System.Numerics;
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

public record RegisterRouteCommand(Guid DriverId, GeoPointDto Start, GeoPointDto End);

public class RegisterRouteHandler(
    IAccountsService accounts,
    IGeoService geo,
    IDriverRouteRepository driverRoutes,
    IRouteJobRepository jobs,
    IComputePublisher<ComputeJob> queue,
    TripPlannerMetrics metrics,
    ILogger<RegisterRouteHandler> logger)
{
    public async Task<RegisterRouteResponse> HandleAsync(RegisterRouteCommand cmd, CancellationToken ct)
    {
        logger.LogDebug("RegisterRoute: driverId={DriverId} start={Start} end={End}", cmd.DriverId, cmd.Start, cmd.End);

        await Validate(cmd, ct);

        var start = new GeoPoint(cmd.Start.Lat, cmd.Start.Lng);
        var end   = new GeoPoint(cmd.End.Lat,   cmd.End.Lng);

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
        logger.LogDebug("RegisterRoute: persisted jobId={JobId} routeId={RouteId}", job.Id, route.Id);

        await queue.PublishAsync(computeJob, ct);
        logger.LogDebug("RegisterRoute: published compute job correlationId={CorrelationId}", correlationId);

        metrics.RouteRegistered();
        logger.LogInformation("Driver {DriverId} registered route, jobId={JobId}", cmd.DriverId, job.Id);

        return new RegisterRouteResponse(job.Id);
    }

    private async Task Validate(RegisterRouteCommand cmd, CancellationToken ct)
    {
        logger.LogDebug("RegisterRoute: validating driverId={DriverId}", cmd.DriverId);

        if (!await accounts.IsDriverActiveAsync(cmd.DriverId, ct))
        {
            logger.LogWarning("RegisterRoute: accounts unavailable for driverId={DriverId}", cmd.DriverId);
            throw new AccountsUnavailableException();
        }

        var start = new GeoPoint(cmd.Start.Lat, cmd.Start.Lng);
        var end   = new GeoPoint(cmd.End.Lat,   cmd.End.Lng);

        if (!await geo.IsWithinServiceAreaAsync(start, ct) || !await geo.IsWithinServiceAreaAsync(end, ct))
        {
            logger.LogWarning("RegisterRoute: points outside service area for driverId={DriverId}", cmd.DriverId);
            throw new OutsideServiceAreaException();
        }

        if (await driverRoutes.GetActiveByDriverIdAsync(cmd.DriverId, ct) is not null)
        {
            logger.LogWarning("RegisterRoute: already has active route driverId={DriverId}", cmd.DriverId);
            throw new RouteAlreadyActiveException();
        }
    }
}
