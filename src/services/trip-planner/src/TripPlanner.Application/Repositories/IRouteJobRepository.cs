using TripPlanner.Domain;

namespace TripPlanner.Application.Repositories;

public interface IRouteJobRepository
{
    Task AddAsync(RouteJob job, CancellationToken ct);
    Task<RouteJob?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<RouteJob?> GetByCorrelationIdAsync(Guid correlationId, CancellationToken ct);
    Task UpdateAsync(RouteJob job, CancellationToken ct);

    // For the job-timeout background worker.
    Task<IReadOnlyList<RouteJob>> GetPendingOlderThanAsync(DateTimeOffset threshold, CancellationToken ct);
}
