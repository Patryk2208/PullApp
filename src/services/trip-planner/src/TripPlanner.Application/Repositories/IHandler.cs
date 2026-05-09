using TripPlanner.Domain.Compute;

namespace TripPlanner.Application.Repositories;

public interface IHandler<in T>
{
    Task HandleAsync(T result, CancellationToken ct);
}

public class ResultHandler(IResultRepository repository) : IHandler<ComputeJobResult>
{
    public Task HandleAsync(ComputeJobResult result, CancellationToken ct)
        => repository.StoreResultAsync(result, ct);
}
