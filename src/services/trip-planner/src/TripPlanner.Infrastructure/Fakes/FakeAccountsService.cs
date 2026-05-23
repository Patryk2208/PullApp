using TripPlanner.Application.Services;

namespace TripPlanner.Infrastructure.Fakes;

public class FakeAccountsService : IAccountsService
{
    public Task<bool> IsDriverActiveAsync(Guid driverId, CancellationToken ct)    => Task.FromResult(true);
    public Task<bool> IsPassengerActiveAsync(Guid passengerId, CancellationToken ct) => Task.FromResult(true);
}
