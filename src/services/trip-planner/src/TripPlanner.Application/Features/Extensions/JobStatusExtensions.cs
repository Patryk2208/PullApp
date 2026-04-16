using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TripPlanner.Domain;

namespace TripPlanner.Application.Features.Extensions
{
    public static class JobStatusExtensions
    {
        public static string ToApiString(this JobStatus status)
        {
            return status switch
            {
                JobStatus.Pending => "pending",
                JobStatus.Completed => "completed",
                _ => "unknown"
            };
        }
    }
}
