using TripPlanner.Domain;

namespace TripPlanner.Application.RouteCalculator;

public class ComputePayloadDto
{
    //todo dto from frontend
}

public interface IRouteCalculator
{
    public Task<Guid> SendComputeAsync(ComputePayloadDto payloadDto, CancellationToken ct);
    public Task<ComputeResult?> TryGetResultAsync(Guid id, CancellationToken ct);
}

internal class RouteCalculator : IRouteCalculator
{
    private readonly IQueuePublisher<ComputePayload> _publisher;
    private readonly IResultRepository _repository;
    
    public RouteCalculator(IQueuePublisher<ComputePayload> publisher, IResultRepository repository)
    {
        _publisher = publisher;
        _repository = repository;
    }
    
    public async Task<Guid> SendComputeAsync(ComputePayloadDto payloadDto, CancellationToken ct)
    {
        //todo create and save payload from dto
        var payload = new ComputePayload(); //todo
        await _publisher.PublishAsync(payload, ct);
        
        throw new NotImplementedException();
    }

    public async Task<ComputeResult?> TryGetResultAsync(Guid id, CancellationToken ct)
    {
        return await _repository.TryGetResultAsync(id, ct);
    }
}