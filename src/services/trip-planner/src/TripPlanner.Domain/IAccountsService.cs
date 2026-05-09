using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TripPlanner.Domain
{
    public interface IAccountsService
    {
        Task<bool> IsDriverActive(Guid driverId);
    }
}
