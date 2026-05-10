using System.Text.Json;
using TripPlanner.Application.Exceptions;
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
    ISseHub sseHub)
{
    public async Task<RegisterRouteResponse> HandleAsync(ModifyRouteCommand cmd, CancellationToken ct)
    {
        var existing = await driverRoutes.GetActiveByDriverIdAsync(cmd.DriverId, ct)
            ?? throw new NotFoundException("no_active_route");

        var activeRide = await rideRequests.GetActiveByPassengerIdAsync(cmd.DriverId, ct);
        if (activeRide is not null)
            throw new CannotModifyDuringRideException();
        
        var start = new GeoPoint(cmd.Start.Lat, cmd.Start.Lng);
        var end   = new GeoPoint(cmd.End.Lat,   cmd.End.Lng);

        if (!await geo.IsWithinServiceAreaAsync(start, ct) ||
            !await geo.IsWithinServiceAreaAsync(end, ct))
            throw new OutsideServiceAreaException();

        // Cancel + re-register (spec §8.2 — always create a fresh job).
        existing.Cancel();
        await driverRoutes.UpdateAsync(existing, ct);

        var affectedRequestIds = await driverRoutes.GetPendingRequestIdsForRouteAsync(existing.Id, ct);
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

        return new RegisterRouteResponse(job.Id);
    }
}
