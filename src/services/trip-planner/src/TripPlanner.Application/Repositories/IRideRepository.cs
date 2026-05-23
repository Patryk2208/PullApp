using TripPlanner.Domain.Ride;

namespace TripPlanner.Application.Repositories;

public interface IRideRepository
{
    Task AddAsync(Ride ride, CancellationToken ct);
    Task<Ride?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Ride?> GetActiveByDriverIdAsync(Guid driverId, CancellationToken ct);
    Task<Ride?> GetActiveByPassengerIdAsync(Guid passengerId, CancellationToken ct);
    Task UpdateAsync(Ride ride, CancellationToken ct);

    // For PriceFreezeMonitorWorker.
    Task<IReadOnlyList<Ride>> GetRidesWithExpiringPriceFreezeAsync(DateTimeOffset threshold, CancellationToken ct);
}
