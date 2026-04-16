using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TripPlanner.Application.Features.Validation
{
    public interface IValidator<T>
    {
        Task Validate(T request);
    }
}
