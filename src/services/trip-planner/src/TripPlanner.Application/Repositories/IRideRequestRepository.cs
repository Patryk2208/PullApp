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

    Task UpdateAsync(RideRequest request, CancellationToken ct);
}
