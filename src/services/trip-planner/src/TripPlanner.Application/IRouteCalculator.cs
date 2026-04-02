namespace TripPlanner.Application;

public class ComputePayload
{
}

public class ComputeResult
{
}

public interface IRouteCalculator
{
    public Task<Guid> SendComputeAsync(ComputePayload payload);
    public Task<ComputeResult?> TryGetResultAsync(Guid id);
}