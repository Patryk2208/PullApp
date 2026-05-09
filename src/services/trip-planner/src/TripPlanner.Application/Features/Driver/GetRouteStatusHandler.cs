using System.Text.Json;
using TripPlanner.Application.Exceptions;
using TripPlanner.Application.Repositories;
using TripPlanner.Domain;
using TripPlanner.Application.Features.DTO.Driver;

namespace TripPlanner.Application.Features.Driver;

public record GetRouteStatusQuery(Guid JobId, Guid DriverId);

public class GetRouteStatusHandler(
    IRouteJobRepository jobs,
    IDriverRouteRepository driverRoutes,
    IRouteCalculator calculator)
{
    public async Task<RouteJobStatusResponse> HandleAsync(GetRouteStatusQuery query, CancellationToken ct)
    {
        var job = await jobs.GetByIdAsync(query.JobId, ct)
            ?? throw new NotFoundException("job_not_found");

        if (job.RequesterId != query.DriverId)
            throw new ForbiddenException("forbidden");

        if (job.Status == JobStatus.Completed)
        {
            var route = await driverRoutes.GetByIdAsync(
                GetRouteIdFromResult(job.ResultJson), ct);

            return new RouteJobStatusResponse(
                "completed",
                route?.Id,
                route?.RouteGeometryJson is not null
                    ? JsonSerializer.Deserialize<object>(route.RouteGeometryJson)
                    : null,
                route?.EtaSeconds,
                route?.DistanceMeters,
                null);
        }

        if (job.Status == JobStatus.Failed)
            return new RouteJobStatusResponse("failed", null, null, null, null, job.ErrorReason);

        // Still pending — try to pull a completed result from the cache.
        var result = await calculator.TryGetResultAsync(job.CorrelationId, ct);
        if (result is Domain.Compute.DriverRouteComputeResult drr)
        {
            // TODO: persist route activation — this is handled by ResultsQueueConsumer in production.
            // For now return the cached data directly so polling works in dev.
            return new RouteJobStatusResponse(
                "completed",
                null,
                JsonSerializer.Deserialize<object>(drr.Result.RouteGeomJson),
                drr.Result.EtaSeconds,
                drr.Result.DistanceMeters,
                null);
        }

        return new RouteJobStatusResponse("pending", null, null, null, null, null);
    }

    private static Guid GetRouteIdFromResult(string? resultJson)
    {
        if (string.IsNullOrEmpty(resultJson)) return Guid.Empty;
        try
        {
            var doc = JsonDocument.Parse(resultJson);
            if (doc.RootElement.TryGetProperty("route_id", out var el) && el.TryGetGuid(out var id))
                return id;
        }
        catch { /* ignore */ }
        return Guid.Empty;
    }
}
