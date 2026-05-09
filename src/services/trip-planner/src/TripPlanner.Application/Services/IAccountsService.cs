namespace TripPlanner.Application.Services;

public interface IAccountsService
{
    Task<bool> IsDriverActiveAsync(Guid driverId, CancellationToken ct);
    Task<bool> IsPassengerActiveAsync(Guid passengerId, CancellationToken ct);
}
