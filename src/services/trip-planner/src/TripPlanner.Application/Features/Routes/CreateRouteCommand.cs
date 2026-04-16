using System;
using System.Collections.Generic;
using System.Text;

namespace TripPlanner.Application.Features.Routes
{
    public class CreateRouteCommand
    {
        public Guid DriverId { get; set; }
        public string Start { get; set; } = default!;
        public string End { get; set; } = default!;
    }
}
