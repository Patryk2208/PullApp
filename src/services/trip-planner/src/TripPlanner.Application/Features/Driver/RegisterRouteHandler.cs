using TripPlanner.Application.Exceptions;
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
    IRouteCalculator calculator)
{
    public async Task<RegisterRouteResponse> HandleAsync(RegisterRouteCommand cmd, CancellationToken ct)
    {
        if (!await accounts.IsDriverActiveAsync(cmd.DriverId, ct))
            throw new AccountsUnavailableException();

        var start = new GeoPoint(cmd.Start.Lat, cmd.Start.Lng);
        var end   = new GeoPoint(cmd.End.Lat,   cmd.End.Lng);

        if (!await geo.IsWithinServiceAreaAsync(start, ct) ||
            !await geo.IsWithinServiceAreaAsync(end, ct))
            throw new OutsideServiceAreaException();

        if (await driverRoutes.GetActiveByDriverIdAsync(cmd.DriverId, ct) is not null)
            throw new RouteAlreadyActiveException();

        var correlationId = Guid.NewGuid();

        var job = new RouteJob
        {
            Id            = Guid.NewGuid(),
            CorrelationId = correlationId,
            JobType       = JobType.DriverRoute,
            RequesterId   = cmd.DriverId,
            PayloadJson   = $"{{\"start\":{{\"lat\":{start.Latitude},\"lon\":{start.Longitude}}},\"end\":{{\"lat\":{end.Latitude},\"lon\":{end.Longitude}}}}}",
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

        await calculator.SendComputeAsync(
            new DriverRouteComputeJob(correlationId, cmd.DriverId, new DriverRouteJobPayload(start, end), DateTimeOffset.UtcNow),
            ct);

        return new RegisterRouteResponse(job.Id);
    }
}
