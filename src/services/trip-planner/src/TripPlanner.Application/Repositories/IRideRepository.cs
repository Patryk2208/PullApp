using TripPlanner.Domain.Ride;

namespace TripPlanner.Application.Repositories;

public interface IRideRepository
{
    Task AddAsync(Ride ride, CancellationToken ct);
    Task<Ride?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Ride>> GetActiveByRouteIdAsync(Guid routeId, CancellationToken ct);
    Task UpdateAsync(Ride ride, CancellationToken ct);
}
