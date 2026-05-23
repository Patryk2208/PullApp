using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Channels;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Infrastructure.Fakes
{
    public class FakeComputeQueue
    {
        public Channel<ComputeJob> Queue { get; } =
            Channel.CreateUnbounded<ComputeJob>();
    }
}
