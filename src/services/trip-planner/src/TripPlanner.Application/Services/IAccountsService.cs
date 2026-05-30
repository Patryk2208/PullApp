namespace TripPlanner.Application.Services;

public interface IAccountsService
{
    // Verifies the driver has required permissions (license, active status, etc.).
    Task<bool> CanDriveAsync(Guid driverId, CancellationToken ct);
}
