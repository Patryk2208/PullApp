using TripPlanner.Domain;

namespace TripPlanner.Application.RouteCalculator;

public interface IMessageHandler<in T>
{
    Task HandleAsync(T result, CancellationToken ct);
}

public class ResultMessageHandler : IMessageHandler<ComputeResult>
{
    private readonly IResultRepository _repository;
    
    public ResultMessageHandler(IResultRepository repository)
    {
        _repository = repository;
    }
    
    public async Task HandleAsync(ComputeResult result, CancellationToken ct)
    {
        await _repository.StoreResultAsync(result, ct);
    }
}