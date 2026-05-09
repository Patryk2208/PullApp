using TripPlanner.Domain.Compute;

namespace TripPlanner.Application.Repositories;

public interface IRouteCalculator
{
    // Dispatches a compute job to Route-Calc via RabbitMQ. Returns the job UUID.
    Task<Guid> SendComputeAsync(ComputeJob job, CancellationToken ct);

    // Attempts to retrieve a previously dispatched result from the cache.
    Task<ComputeJobResult?> TryGetResultAsync(Guid jobId, CancellationToken ct);
}

internal class RouteCalculator(
    IPublisher<ComputeJob> publisher,
    IResultRepository repository) : IRouteCalculator
{
    public async Task<Guid> SendComputeAsync(ComputeJob job, CancellationToken ct)
    {
        await publisher.PublishAsync(job, ct);
        return job.JobId;
    }

    public Task<ComputeJobResult?> TryGetResultAsync(Guid jobId, CancellationToken ct)
        => repository.TryGetResultAsync(jobId, ct);
}
