using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TripPlanner.Domain;

namespace TripPlanner.Infrastructure.Fakes
{
    public class FakeAccountsService : IAccountsService
    {
        public Task<bool> IsDriverActive(Guid driverId)
        {
            return Task.FromResult(true);
        }
    }
}
