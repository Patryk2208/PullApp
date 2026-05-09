using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TripPlanner.Application.Features.Routes;
using TripPlanner.Domain;

namespace TripPlanner.Application.Features.Validation.Validators
{
    public class GeoValidator : IValidator<CreateRouteCommand>
    {
        private readonly IGeoService _geo;

        public GeoValidator(IGeoService geo)
        {
            _geo = geo;
        }

        public Task Validate(CreateRouteCommand cmd)
        {
            if (!_geo.IsWithinArea(cmd.Start, cmd.End))
                throw new InvalidOperationException("Outside service area");

            return Task.CompletedTask;
        }
    }
}
