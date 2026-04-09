using TripPlanner.Domain;

namespace TripPlanner.Application.RouteCalculator;

public interface IMessageHandler<in T>
{
    Task HandleAsync(T result, CancellationToken ct);
}

public class ResultMessageHandler(IResultRepository repository) : IMessageHandler<ComputeResult>
{
    public async Task HandleAsync(ComputeResult result, CancellationToken ct)
    {
        await repository.StoreResultAsync(result, ct);
    }
}