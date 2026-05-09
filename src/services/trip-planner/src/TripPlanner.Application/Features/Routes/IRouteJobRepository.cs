using System;
using System.Collections.Generic;
using System.Text;
using TripPlanner.Domain;

namespace TripPlanner.Application.Features.Routes
{
    public interface IRouteJobRepository
    {
        Task Add(RouteJob job);
        Task<RouteJob?> Get(Guid id);
        Task Update(RouteJob job);
    }
}
