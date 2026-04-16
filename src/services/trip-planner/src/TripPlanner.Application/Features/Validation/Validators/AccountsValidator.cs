using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TripPlanner.Application.Features.Routes;
using TripPlanner.Domain;

namespace TripPlanner.Application.Features.Validation.Validators
{
    public class AccountsValidator : IValidator<CreateRouteCommand>
    {
        private readonly IAccountsService _accounts;

        public AccountsValidator(IAccountsService accounts)
        {
            _accounts = accounts;
        }

        public async Task Validate(CreateRouteCommand cmd)
        {
            var isActive = await _accounts.IsDriverActive(cmd.DriverId);

            if (!isActive)
                throw new Exception("Driver not active");
        }
    }
}
