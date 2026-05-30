namespace TripPlanner.UnitTests.Domain;

public class RideTests
{
    private static Ride NewRide(bool routeIsActive = true) =>
        Ride.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new GeoPoint(52.2, 21.0), new GeoPoint(52.3, 21.1),
            price: 20m, cancellationPrice: 4m, frozenPriceId: Guid.NewGuid(),
            routeIsActive: routeIsActive);

    // ─── Create ──────────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithActiveRoute_SetsWaitingForDriver()
    {
        var ride = NewRide(routeIsActive: true);

        Assert.Equal(RideStatus.WaitingForDriver, ride.Status);
        Assert.False(ride.DriverDeclaredPickup);
        Assert.False(ride.PassengerDeclaredPickup);
        Assert.Null(ride.StartedAt);
        Assert.Null(ride.EndedAt);
    }

    [Fact]
    public void Create_WithInactiveRoute_SetsWaitingForActivation()
    {
        var ride = NewRide(routeIsActive: false);

        Assert.Equal(RideStatus.WaitingForActivation, ride.Status);
    }

    // ─── ActivateIfWaiting ───────────────────────────────────────────────────

    [Fact]
    public void ActivateIfWaiting_WhenWaitingForActivation_TransitionsToWaitingForDriver()
    {
        var ride = NewRide(routeIsActive: false);

        ride.ActivateIfWaiting();

        Assert.Equal(RideStatus.WaitingForDriver, ride.Status);
    }

    [Fact]
    public void ActivateIfWaiting_WhenAlreadyWaitingForDriver_IsNoOp()
    {
        var ride = NewRide(routeIsActive: true);

        ride.ActivateIfWaiting();

        Assert.Equal(RideStatus.WaitingForDriver, ride.Status);
    }

    // ─── Pickup declarations ─────────────────────────────────────────────────

    [Fact]
    public void DeclareDriverPickup_SetsFlag_StatusRemainsWaitingForDriver()
    {
        var ride = NewRide();

        ride.DeclareDriverPickup();

        Assert.True(ride.DriverDeclaredPickup);
        Assert.Equal(RideStatus.WaitingForDriver, ride.Status);
        Assert.Null(ride.StartedAt);
    }

    [Fact]
    public void DeclarePassengerPickup_WithoutDriverDeclaration_ReturnsFalse()
    {
        var ride = NewRide();

        var result = ride.DeclarePassengerPickup();

        Assert.False(result);
        Assert.False(ride.PassengerDeclaredPickup);
    }

    [Fact]
    public void DeclarePassengerPickup_AfterDriverDeclaration_ReturnsTrueAndStartsRide()
    {
        var ride = NewRide();
        ride.DeclareDriverPickup();

        var result = ride.DeclarePassengerPickup();

        Assert.True(result);
        Assert.True(ride.PassengerDeclaredPickup);
        Assert.Equal(RideStatus.Started, ride.Status);
        Assert.NotNull(ride.StartedAt);
    }

    [Fact]
    public void BothPickupDeclarations_RequiredToStartRide()
    {
        var ride = NewRide();

        ride.DeclareDriverPickup();
        Assert.Equal(RideStatus.WaitingForDriver, ride.Status);

        ride.DeclarePassengerPickup();
        Assert.Equal(RideStatus.Started, ride.Status);
    }

    // ─── End declarations ────────────────────────────────────────────────────

    [Fact]
    public void DeclarePassengerEnd_WhenStarted_SetsFlag()
    {
        var ride = NewRide();
        ride.DeclareDriverPickup();
        ride.DeclarePassengerPickup();

        var result = ride.DeclarePassengerEnd();

        Assert.True(result);
        Assert.True(ride.PassengerDeclaredEnd);
        Assert.Null(ride.EndedAt); // not ended yet — driver still needs to declare
    }

    [Fact]
    public void DeclareDriverEnd_WithoutPassengerDeclaration_ReturnsFalse()
    {
        var ride = NewRide();
        ride.DeclareDriverPickup();
        ride.DeclarePassengerPickup();

        var result = ride.DeclareDriverEnd();

        Assert.False(result);
        Assert.Null(ride.EndedAt);
    }

    [Fact]
    public void DeclareDriverEnd_AfterPassengerDeclaration_ReturnsTrueAndSetsEndedAt()
    {
        var ride = NewRide();
        ride.DeclareDriverPickup();
        ride.DeclarePassengerPickup();
        ride.DeclarePassengerEnd();

        var result = ride.DeclareDriverEnd();

        Assert.True(result);
        Assert.True(ride.DriverDeclaredEnd);
        Assert.NotNull(ride.EndedAt);
        Assert.True(ride.IsEnded);
    }

    // ─── Cancel ──────────────────────────────────────────────────────────────

    [Fact]
    public void Cancel_SetsEndedAt()
    {
        var ride = NewRide();

        ride.Cancel();

        Assert.NotNull(ride.EndedAt);
        Assert.True(ride.IsEnded);
    }

    [Fact]
    public void IsEnded_FalseByDefault()
    {
        var ride = NewRide();

        Assert.False(ride.IsEnded);
    }
}
