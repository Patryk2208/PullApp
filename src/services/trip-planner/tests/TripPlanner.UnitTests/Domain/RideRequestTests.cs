namespace TripPlanner.UnitTests.Domain;

public class RideRequestTests
{
    private static RideRequest NewRequest() =>
        RideRequest.Create(Guid.NewGuid(), Guid.NewGuid(),
            new GeoPoint(52.2, 21.0), new GeoPoint(52.3, 21.1));

    [Fact]
    public void Create_SetsPendingStatusAndFields()
    {
        var routeId     = Guid.NewGuid();
        var passengerId = Guid.NewGuid();
        var start       = new GeoPoint(52.2, 21.0);
        var end         = new GeoPoint(52.3, 21.1);

        var req = RideRequest.Create(routeId, passengerId, start, end);

        Assert.NotEqual(Guid.Empty, req.Id);
        Assert.Equal(routeId,                   req.RouteId);
        Assert.Equal(passengerId,               req.PassengerId);
        Assert.Equal(RideRequestStatus.Pending, req.Status);
        Assert.Null(req.FrozenPriceId);
        Assert.Equal(0m, req.Price);
        Assert.Equal(0m, req.CancellationPrice);
        Assert.Null(req.RejectedAt);
    }

    [Fact]
    public void SetFrozenPrice_StoresAllThreeFields()
    {
        var req      = NewRequest();
        var frozenId = Guid.NewGuid();

        req.SetFrozenPrice(frozenId, price: 18.50m, cancellationPrice: 3.75m);

        Assert.Equal(frozenId, req.FrozenPriceId);
        Assert.Equal(18.50m,   req.Price);
        Assert.Equal(3.75m,    req.CancellationPrice);
    }

    [Fact]
    public void Accept_SetsAcceptedStatus()
    {
        var req = NewRequest();

        req.Accept();

        Assert.Equal(RideRequestStatus.Accepted, req.Status);
    }

    [Fact]
    public void Reject_SetsRejectedStatusAndStoresTimestamp()
    {
        var req    = NewRequest();
        var before = DateTimeOffset.UtcNow;

        req.Reject();

        Assert.Equal(RideRequestStatus.Rejected, req.Status);
        Assert.NotNull(req.RejectedAt);
        Assert.True(req.RejectedAt >= before);
    }
}
