using System;
using System.Collections.Generic;
using System.Text;
using TripPlanner.Application.Features.Routes;
using TripPlanner.Domain;

namespace TripPlanner.Infrastructure
{
    public class InMemoryRouteJobRepository : IRouteJobRepository
    {
        private static readonly Dictionary<Guid, RouteJob> _db = new();

        public Task Add(RouteJob job)
        {
            _db[job.Id] = job;
            return Task.CompletedTask;
        }

        public Task<RouteJob?> Get(Guid id)
        {
            _db.TryGetValue(id, out var job);
            return Task.FromResult(job);
        }

        public Task Update(RouteJob job)
        {
            _db[job.Id] = job;
            return Task.CompletedTask;
        }
    }
}
