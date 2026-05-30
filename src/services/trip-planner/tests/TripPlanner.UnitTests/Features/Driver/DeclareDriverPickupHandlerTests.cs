using TripPlanner.Application.Features.Driver;

namespace TripPlanner.UnitTests.Features.Driver;

public class DeclareDriverPickupHandlerTests
{
    private readonly IRideRepository _rides = Substitute.For<IRideRepository>();
    private readonly IUnitOfWork     _uow   = Substitute.For<IUnitOfWork>();

    private DeclareDriverPickupHandler Handler() => new(_rides, _uow);

    [Fact]
    public async Task HandleAsync_HappyPath_SetsDriverDeclaredPickup()
    {
        var driverId = Guid.NewGuid();
        var ride     = Make.WaitingForDriverRide(driverId: driverId);
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);

        await Handler().HandleAsync(new(driverId, ride.Id), default);

        Assert.True(ride.DriverDeclaredPickup);
        await _rides.Received(1).UpdateAsync(ride, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RideNotFound_ThrowsRideNotFoundException()
    {
        _rides.GetByIdAsync(Arg.Any<Guid>(), default).Returns((Ride?)null);

        await Assert.ThrowsAsync<RideNotFoundException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), Guid.NewGuid()), default));
    }

    [Fact]
    public async Task HandleAsync_WrongDriver_ThrowsUnauthorizedException()
    {
        var ride = Make.WaitingForDriverRide();
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), ride.Id), default));
    }

    [Fact]
    public async Task HandleAsync_NotWaitingForDriver_ThrowsInvalidRouteStatusException()
    {
        var driverId = Guid.NewGuid();
        var ride     = Make.WaitingForActivationRide(driverId: driverId);
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);

        await Assert.ThrowsAsync<InvalidRouteStatusException>(() =>
            Handler().HandleAsync(new(driverId, ride.Id), default));
    }
}
