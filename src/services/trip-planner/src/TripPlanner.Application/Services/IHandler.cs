using TripPlanner.Application.Repositories;
using TripPlanner.Domain.Compute;

namespace TripPlanner.Application.Services;

public interface IHandler<in T>
{
    Task HandleAsync(T result, CancellationToken ct);
}
