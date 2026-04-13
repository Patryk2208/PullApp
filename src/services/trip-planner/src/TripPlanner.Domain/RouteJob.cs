using System;
using System.Collections.Generic;
using System.Text;

namespace TripPlanner.Domain
{
    public enum JobStatus
    {
        Pending,
        Completed
    }

    public class RouteJob
    {
        public Guid Id { get; set; }
        public Guid ExternalJobId { get; set; } // from the route calculator
        public JobStatus Status { get; set; }

        public string? Route { get; set; }
    }
}
