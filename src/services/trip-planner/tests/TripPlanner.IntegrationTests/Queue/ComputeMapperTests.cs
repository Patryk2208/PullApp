using Core.V1;
using Google.Protobuf;
using TripPlanner.Domain.Compute;
using TripPlanner.Infrastructure.Queue;

namespace TripPlanner.IntegrationTests.Queue;

/// <summary>
/// Verifies the protobuf mappers used to communicate with route-calc.
/// No containers needed — pure serialization round-trips.
/// </summary>
public class ComputeMapperTests
{
    private readonly ComputeJobProtoMapper    _jobMapper    = new();
    private readonly ComputeResultProtoMapper _resultMapper = new();

    // ─── ComputeJobProtoMapper (trip-planner → route-calc) ───────────────────

    [Fact]
    public void JobMapper_BestRoute_SerializesAllFields()
    {
        var jobId  = Guid.NewGuid();
        var driver = Guid.NewGuid();
        var job    = new BestRouteComputeJob(
            JobId:    jobId,
            DriverId: driver,
            Payload:  new BestRouteJobPayload(new GeoPoint(52.2, 21.0), new GeoPoint(52.3, 21.1)),
            CreatedAt: DateTimeOffset.UtcNow,
            RetryCount: 2);

        var bytes = _jobMapper.ToDto(job);
        var msg   = ComputeMessage.Parser.ParseFrom(bytes.Span);

        Assert.Equal(jobId.ToString(),  msg.JobId);
        Assert.Equal("best_route",      msg.Algorithm);
        Assert.Equal(driver.ToString(), msg.RequestingUserId);
        Assert.Equal(2,                 msg.RetryCount);
        Assert.Equal(52.2, msg.BestRoute.Start.Lat, 6);
        Assert.Equal(21.0, msg.BestRoute.Start.Lon, 6);
        Assert.Equal(52.3, msg.BestRoute.End.Lat, 6);
        Assert.Equal(21.1, msg.BestRoute.End.Lon, 6);
        Assert.Equal("distance", msg.BestRoute.CostType);
    }

    [Fact]
    public void JobMapper_RideMatching_SerializesAllFields()
    {
        var jobId     = Guid.NewGuid();
        var passenger = Guid.NewGuid();
        var job       = new RideMatchingComputeJob(
            JobId:       jobId,
            PassengerId: passenger,
            Payload:     new RideMatchingJobPayload(
                new GeoPoint(52.2, 21.0),
                new GeoPoint(52.3, 21.1),
                DepartureDate: 1_700_000_000,
                SeatsNeeded: 2,
                MaxDetourKm: 15),
            CreatedAt: DateTimeOffset.UtcNow);

        var bytes = _jobMapper.ToDto(job);
        var msg   = ComputeMessage.Parser.ParseFrom(bytes.Span);

        Assert.Equal(jobId.ToString(),     msg.JobId);
        Assert.Equal("ride_matching",      msg.Algorithm);
        Assert.Equal(passenger.ToString(), msg.RequestingUserId);
        Assert.Equal(52.2, msg.RideMatching.Start.Lat, 6);
        Assert.Equal(21.1, msg.RideMatching.End.Lon, 6);
        Assert.Equal(1_700_000_000,        msg.RideMatching.DepartureDate);
        Assert.Equal(2,                    msg.RideMatching.SeatsNeeded);
        Assert.Equal(15,                   msg.RideMatching.MaxDetourKm);
        Assert.Equal(passenger.ToString(), msg.RideMatching.PassengerId);
    }

    // ─── ComputeResultProtoMapper (route-calc → trip-planner) ────────────────

    [Fact]
    public void ResultMapper_BestRoute_DeserializesAllFields()
    {
        var jobId = Guid.NewGuid();
        var proto = new ResultMessage
        {
            JobId   = jobId.ToString(),
            Success = true,
            BestRoute = new BestRouteResult
            {
                DistanceMeters  = 15000,
                DurationSeconds = 900,
            },
        };
        proto.BestRoute.Points.Add(new Point { Lat = 52.2, Lon = 21.0 });
        proto.BestRoute.Points.Add(new Point { Lat = 52.3, Lon = 21.1 });

        var result = _resultMapper.ToDomain(proto.ToByteArray());

        var br = Assert.IsType<BestRouteComputeResult>(result);
        Assert.Equal(jobId,   br.JobId);
        Assert.Equal(15000,   br.Result.DistanceMeters, 6);
        Assert.Equal(900,     br.Result.DurationSeconds, 6);
        Assert.Equal(2,       br.Result.Points.Count);
        Assert.Equal(52.2,    br.Result.Points[0].Latitude, 6);
    }

    [Fact]
    public void ResultMapper_RideMatching_DeserializesAllFields()
    {
        var jobId    = Guid.NewGuid();
        var routeId  = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var proto    = new ResultMessage
        {
            JobId   = jobId.ToString(),
            Success = true,
            RideMatching = new RideMatchingResult
            {
                Matches =
                {
                    new MatchedRoute
                    {
                        RouteId            = routeId.ToString(),
                        DriverId           = driverId.ToString(),
                        MatchScore         = 0.92,
                        DetourKm           = 1.5,
                        PickupPointIndex   = 0,
                        DropoffPointIndex  = 3,
                    },
                },
            },
        };

        var result = _resultMapper.ToDomain(proto.ToByteArray());

        var rm = Assert.IsType<RideMatchingComputeResult>(result);
        Assert.Equal(jobId, rm.JobId);
        Assert.Single(rm.Result.Matches);

        var match = rm.Result.Matches[0];
        Assert.Equal(routeId.ToString(),  match.RouteId);
        Assert.Equal(driverId.ToString(), match.DriverId);
        Assert.Equal(0.92,  match.MatchScore, 6);
        Assert.Equal(1.5,   match.DetourKm, 6);
        Assert.Equal(0,     match.PickupPointIndex);
        Assert.Equal(3,     match.DropoffPointIndex);
    }

    [Fact]
    public void ResultMapper_Failed_ReturnsFailedResult()
    {
        var jobId = Guid.NewGuid();
        var proto = new ResultMessage
        {
            JobId   = jobId.ToString(),
            Success = false,
            Error   = "osrm_timeout",
        };

        var result = _resultMapper.ToDomain(proto.ToByteArray());

        var failed = Assert.IsType<FailedComputeResult>(result);
        Assert.Equal(jobId,          failed.JobId);
        Assert.Equal("osrm_timeout", failed.Error);
        Assert.False(failed.Success);
    }
}