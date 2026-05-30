using TripPlanner.Domain.Route;

namespace TripPlanner.Application.Repositories;

public interface IRouteRepository
{
    Task AddAsync(Route route, CancellationToken ct);
    Task<Route?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Route?> GetByIdForUpdateAsync(Guid id, CancellationToken ct);
    Task<Route?> GetActiveByDriverIdAsync(Guid driverId, CancellationToken ct);
    Task UpdateAsync(Route route, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}
