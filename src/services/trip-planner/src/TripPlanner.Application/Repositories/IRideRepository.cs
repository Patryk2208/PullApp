using TripPlanner.Domain.Ride;

namespace TripPlanner.Application.Repositories;

public interface IRideRepository
{
    Task AddAsync(Ride ride, CancellationToken ct);
    Task<Ride?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Ride>> GetActiveByRouteIdAsync(Guid routeId, CancellationToken ct);

    // Read-model queries — all rides for a passenger / a driver, newest first.
    Task<IReadOnlyList<Ride>> GetByPassengerIdAsync(Guid passengerId, CancellationToken ct);
    Task<IReadOnlyList<Ride>> GetByDriverIdAsync(Guid driverId, CancellationToken ct);

    Task UpdateAsync(Ride ride, CancellationToken ct);
    Task DeleteByRouteIdAsync(Guid routeId, CancellationToken ct);
}
