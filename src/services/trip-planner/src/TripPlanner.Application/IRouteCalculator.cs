using TripPlanner.Domain;

namespace TripPlanner.Application;

public interface IRouteCalculator
{
    public Task<Guid> SendComputeAsync(ComputePayload payload);
    public Task<ComputeResult?> TryGetResultAsync(Guid id);
}