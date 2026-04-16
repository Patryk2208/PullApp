using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TripPlanner.Domain;

namespace TripPlanner.Infrastructure.Fakes
{
    public class FakeGeoService : IGeoService
    {
        public bool IsWithinArea(string start, string end)
        {
            return true; // hardcoded 
        }
    }
}
