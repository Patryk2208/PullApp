using TripPlanner.Domain;

namespace TripPlanner.Infrastructure.Queue;

public interface IComputePublisher
{
    void RequestCompute(ComputePayload payload);
}

internal class ComputePublisher : IComputePublisher
{
    public void RequestCompute(ComputePayload payload)
    {
        throw new NotImplementedException();
    }
}