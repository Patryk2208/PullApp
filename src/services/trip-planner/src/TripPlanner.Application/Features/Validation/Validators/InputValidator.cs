using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TripPlanner.Application.Features.Routes;

namespace TripPlanner.Application.Features.Validation.Validators
{
    public class CreateRouteInputValidator : IValidator<CreateRouteCommand>
    {
        public Task Validate(CreateRouteCommand cmd)
        {
            if (cmd.DriverId == Guid.Empty)
                throw new Exception("DriverId required");

            if (string.IsNullOrWhiteSpace(cmd.Start))
                throw new Exception("Start required");

            if (string.IsNullOrWhiteSpace(cmd.End))
                throw new Exception("End required");

            return Task.CompletedTask;
        }
    }
}
