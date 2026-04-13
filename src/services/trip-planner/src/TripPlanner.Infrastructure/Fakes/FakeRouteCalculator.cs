using System;
using System.Collections.Generic;
using System.Text;
using TripPlanner.Application;

namespace TripPlanner.Infrastructure.Fakes
{
    public class FakeRouteCalculator : IRouteCalculator
    {
        private static readonly Dictionary<Guid, ComputeResult> _results = new();

        public Task<Guid> SendComputeAsync(ComputePayload payload)
        {
            var id = Guid.NewGuid();

            // Simulating computation
            _ = Task.Run(async () =>
            {
                await Task.Delay(10000);
                _results[id] = new ComputeResult();
            });

            return Task.FromResult(id);
        }

        public Task<ComputeResult?> TryGetResultAsync(Guid id)
        {
            _results.TryGetValue(id, out var result);
            return Task.FromResult(result);
        }
    }
}
