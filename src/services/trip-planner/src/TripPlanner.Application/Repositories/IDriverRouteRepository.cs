using TripPlanner.Domain.Driver;

namespace TripPlanner.Application.Repositories;

public interface IDriverRouteRepository
{
    Task AddAsync(DriverRoute route, CancellationToken ct);
    Task<DriverRoute?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<DriverRoute?> GetActiveByDriverIdAsync(Guid driverId, CancellationToken ct);
    Task UpdateAsync(DriverRoute route, CancellationToken ct);

    // Returns all requests in routes_presented state that include this route in their match_results.
    Task<IReadOnlyList<Guid>> GetPendingRequestIdsForRouteAsync(Guid driverRouteId, CancellationToken ct);
}
