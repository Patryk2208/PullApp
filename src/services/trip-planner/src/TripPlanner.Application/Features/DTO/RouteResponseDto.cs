using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TripPlanner.Application.Features.DTO
{
    public class RouteResponseDto
    {
        public string Status { get; set; } = default!;
        public string? Route { get; set; }
    }
}
