using TripPlanner.Application.Services;

namespace TripPlanner.Infrastructure.Fakes;

public class FakeAccountsService : IAccountsService
{
    public Task<bool> CanDriveAsync(Guid driverId, CancellationToken ct) => Task.FromResult(true);
}
