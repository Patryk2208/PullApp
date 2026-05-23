using TripPlanner.Domain.Passenger;

namespace TripPlanner.Application.Repositories;

public interface IRideRequestRepository
{
    Task AddAsync(RideRequest request, CancellationToken ct);
    Task<RideRequest?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<RideRequest?> GetActiveByPassengerIdAsync(Guid passengerId, CancellationToken ct);
    Task UpdateAsync(RideRequest request, CancellationToken ct);

    // For ConfirmationMonitorWorker — all pending_driver rows past their deadline.
    Task<IReadOnlyList<RideRequest>> GetExpiredConfirmationsAsync(CancellationToken ct);
}
