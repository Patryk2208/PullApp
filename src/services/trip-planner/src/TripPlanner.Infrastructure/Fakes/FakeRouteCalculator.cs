using System;
using System.Collections.Generic;
using System.Text;
using TripPlanner.Application;
using TripPlanner.Application.RouteCalculator;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Infrastructure.Fakes
{
    public class FakeRouteCalculator : IRouteCalculator
    {
        private static readonly Dictionary<Guid, ComputeResult> _results = new();

        public Task<Guid> SendComputeAsync(ComputePayload payload, CancellationToken ct)
        {
            var id = Guid.NewGuid();

            // Simulating computation
            _ = Task.Run(async () =>
            {
                await Task.Delay(10000, ct);
                _results[id] = new ComputeResult();
            }, ct);

            return Task.FromResult(id);
        }

        public Task<ComputeResult?> TryGetResultAsync(Guid id, CancellationToken ct)
        {
            _results.TryGetValue(id, out var result);
            return Task.FromResult(result);
        }
    }
}
