using TripPlanner.Domain.RideRequest;

namespace TripPlanner.Application.Repositories;

public interface IRideRequestRepository
{
    Task AddAsync(RideRequest request, CancellationToken ct);
    Task<RideRequest?> GetByIdAsync(Guid id, CancellationToken ct);

    // Returns pending requests for a given route (used in flow 5 to reject others when route becomes full).
    Task<IReadOnlyList<RideRequest>> GetPendingByRouteIdAsync(Guid routeId, CancellationToken ct);

    // Returns rejected requests for a given route (used in flow 8 to notify passengers a ride ended).
    Task<IReadOnlyList<RideRequest>> GetRejectedByRouteIdAsync(Guid routeId, CancellationToken ct);

    // Read-model query — all requests a passenger has made (any status), newest first.
    Task<IReadOnlyList<RideRequest>> GetByPassengerIdAsync(Guid passengerId, CancellationToken ct);

    // Read-model query — pending requests across all of a driver's routes (joins routes on driver_id).
    Task<IReadOnlyList<RideRequest>> GetPendingByDriverIdAsync(Guid driverId, CancellationToken ct);

    Task UpdateAsync(RideRequest request, CancellationToken ct);
}
