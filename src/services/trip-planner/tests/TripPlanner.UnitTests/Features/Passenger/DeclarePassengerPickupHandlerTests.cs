using TripPlanner.Application.Features.Passenger;

namespace TripPlanner.UnitTests.Features.Passenger;

public class DeclarePassengerPickupHandlerTests
{
    private readonly IRideRepository _rides = Substitute.For<IRideRepository>();
    private readonly IUnitOfWork     _uow   = Substitute.For<IUnitOfWork>();

    private DeclarePassengerPickupHandler Handler() => new(_rides, _uow);

    [Fact]
    public async Task HandleAsync_HappyPath_TransitionsRideToStarted()
    {
        var passengerId = Guid.NewGuid();
        var ride        = Make.WaitingForDriverRide(passengerId: passengerId);
        ride.DeclareDriverPickup(); // driver goes first
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);

        await Handler().HandleAsync(new(passengerId, ride.Id), default);

        Assert.Equal(RideStatus.Started, ride.Status);
        Assert.True(ride.PassengerDeclaredPickup);
        await _rides.Received(1).UpdateAsync(ride, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_DriverHasNotDeclared_ThrowsDeclarationOrderException()
    {
        var passengerId = Guid.NewGuid();
        var ride        = Make.WaitingForDriverRide(passengerId: passengerId);
        // driver has NOT declared pickup
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);

        await Assert.ThrowsAsync<DeclarationOrderException>(() =>
            Handler().HandleAsync(new(passengerId, ride.Id), default));
    }

    [Fact]
    public async Task HandleAsync_RideNotFound_ThrowsRideNotFoundException()
    {
        _rides.GetByIdAsync(Arg.Any<Guid>(), default).Returns((Ride?)null);

        await Assert.ThrowsAsync<RideNotFoundException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), Guid.NewGuid()), default));
    }

    [Fact]
    public async Task HandleAsync_WrongPassenger_ThrowsUnauthorizedException()
    {
        var ride = Make.WaitingForDriverRide();
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), ride.Id), default));
    }

    [Fact]
    public async Task HandleAsync_NotWaitingForDriver_ThrowsInvalidRouteStatusException()
    {
        var passengerId = Guid.NewGuid();
        var ride        = Make.WaitingForActivationRide(passengerId: passengerId);
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);

        await Assert.ThrowsAsync<InvalidRouteStatusException>(() =>
            Handler().HandleAsync(new(passengerId, ride.Id), default));
    }
}
