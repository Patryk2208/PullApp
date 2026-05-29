using TripPlanner.Application.Features.Passenger;

namespace TripPlanner.UnitTests.Features.Passenger;

public class DeclarePassengerEndHandlerTests
{
    private readonly IRideRepository _rides = Substitute.For<IRideRepository>();
    private readonly IUnitOfWork     _uow   = Substitute.For<IUnitOfWork>();

    private DeclarePassengerEndHandler Handler() => new(_rides, _uow);

    [Fact]
    public async Task HandleAsync_HappyPath_RecordsPassengerDeclaration()
    {
        var passengerId = Guid.NewGuid();
        var ride        = Make.StartedRide(passengerId: passengerId);
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);

        await Handler().HandleAsync(new(passengerId, ride.Id), default);

        Assert.True(ride.PassengerDeclaredEnd);
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
    public async Task HandleAsync_WrongPassenger_ThrowsUnauthorizedException()
    {
        var ride = Make.StartedRide();
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            Handler().HandleAsync(new(Guid.NewGuid(), ride.Id), default));
    }

    [Fact]
    public async Task HandleAsync_RideNotStarted_ThrowsInvalidRouteStatusException()
    {
        var passengerId = Guid.NewGuid();
        var ride        = Make.WaitingForDriverRide(passengerId: passengerId);
        _rides.GetByIdAsync(ride.Id, default).Returns(ride);

        await Assert.ThrowsAsync<InvalidRouteStatusException>(() =>
            Handler().HandleAsync(new(passengerId, ride.Id), default));
    }
}
