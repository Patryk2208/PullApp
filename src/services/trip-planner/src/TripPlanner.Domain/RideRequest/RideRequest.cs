using TripPlanner.Domain.Compute;

namespace TripPlanner.Domain.RideRequest;

public enum RideRequestStatus { Pending, Accepted, Rejected }

public class RideRequest
{
    public Guid Id { get; private set; }
    public Guid RouteId { get; private set; }
    public Guid PassengerId { get; private set; }
    public GeoPoint StartPoint { get; private set; } = default!;
    public GeoPoint EndPoint { get; private set; } = default!;
    public Guid? FrozenPriceId { get; private set; }
    public RideRequestStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RejectedAt { get; private set; }

    private RideRequest() { }

    public static RideRequest Create(Guid routeId, Guid passengerId, GeoPoint startPoint, GeoPoint endPoint) =>
        new()
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            PassengerId = passengerId,
            StartPoint = startPoint,
            EndPoint = endPoint,
            Status = RideRequestStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    public void SetFrozenPrice(Guid frozenPriceId) => FrozenPriceId = frozenPriceId;

    public void Accept() => Status = RideRequestStatus.Accepted;

    public void Reject()
    {
        Status = RideRequestStatus.Rejected;
        RejectedAt = DateTimeOffset.UtcNow;
    }
}
