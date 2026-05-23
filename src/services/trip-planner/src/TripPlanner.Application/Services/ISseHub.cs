namespace TripPlanner.Application.Services;

public interface ISseHub
{
    // connectionKey is the requestId (during matching) or rideId (during a ride).
    Task PushAsync(Guid connectionKey, string eventType, string jsonPayload, CancellationToken ct);
    Task CloseAsync(Guid connectionKey, CancellationToken ct);
}
