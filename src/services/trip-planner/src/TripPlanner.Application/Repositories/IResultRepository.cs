using TripPlanner.Domain.Compute;

namespace TripPlanner.Application.RouteCalculator;

public interface IResultRepository
{
    Task StoreResultAsync(ComputeJobResult result, CancellationToken ct);
    Task<ComputeJobResult?> TryGetResultAsync(Guid jobId, CancellationToken ct);
}
