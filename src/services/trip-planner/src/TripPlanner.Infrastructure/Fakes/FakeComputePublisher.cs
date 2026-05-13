using System;
using System.Collections.Generic;
using System.Text;
using TripPlanner.Application.Services;

namespace TripPlanner.Infrastructure.Fakes
{
    public class FakeComputePublisher<T> : IComputePublisher<T>
    {
        public Task PublishAsync(T payload, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }
}
