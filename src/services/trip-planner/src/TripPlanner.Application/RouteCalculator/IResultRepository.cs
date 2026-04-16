using TripPlanner.Domain;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Application.RouteCalculator;

public interface IResultRepository
{
    Task StoreResultAsync(ComputeResult result, CancellationToken ct);
    Task<ComputeResult?> TryGetResultAsync(Guid id, CancellationToken ct);
}