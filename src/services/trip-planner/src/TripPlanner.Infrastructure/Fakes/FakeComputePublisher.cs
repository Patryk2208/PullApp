using System;
using System.Collections.Generic;
using System.Text;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Infrastructure.Fakes
{
    public class FakeComputePublisher<T>(
    FakeComputeQueue queue)
    : IComputePublisher<T>
    {
        public async Task PublishAsync(
            T payload,
            CancellationToken ct)
        {
            if (payload is ComputeJob job)
            {
                await queue.Queue.Writer.WriteAsync(job, ct);
            }
        }
    }
}
