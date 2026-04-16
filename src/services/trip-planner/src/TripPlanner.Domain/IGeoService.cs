using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TripPlanner.Domain
{
    public interface IGeoService
    {
        bool IsWithinArea(string start, string end);
    }
}
