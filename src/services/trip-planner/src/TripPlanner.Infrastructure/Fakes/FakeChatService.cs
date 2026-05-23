using TripPlanner.Application.Services;

namespace TripPlanner.Infrastructure.Fakes;

public class FakeChatService : IChatService
{
    public Task<Guid> CreateRoomAsync(Guid rideId, Guid driverId, Guid passengerId, CancellationToken ct)
        => Task.FromResult(Guid.NewGuid());

    public Task CloseRoomAsync(Guid roomId, string reason, CancellationToken ct)
        => Task.CompletedTask;
}
