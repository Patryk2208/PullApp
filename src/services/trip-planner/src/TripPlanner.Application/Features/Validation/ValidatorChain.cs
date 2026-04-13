using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TripPlanner.Application.Features.Validation
{
    public class ValidatorChain<T> : IValidator<T>
    {
        private readonly IEnumerable<IValidator<T>> _validators;

        public ValidatorChain(IEnumerable<IValidator<T>> validators)
        {
            _validators = validators;
        }

        public async Task Validate(T request)
        {
            foreach (var validator in _validators)
            {
                await validator.Validate(request);
            }
        }
    }
}
