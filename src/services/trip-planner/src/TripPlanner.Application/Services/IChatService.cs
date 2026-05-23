namespace TripPlanner.Application.Services;

public interface IChatService
{
    Task<Guid> CreateRoomAsync(Guid rideId, Guid driverId, Guid passengerId, CancellationToken ct);
    Task CloseRoomAsync(Guid roomId, string reason, CancellationToken ct);
}
