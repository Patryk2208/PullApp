using Core.V1;
using Google.Protobuf;
using TripPlanner.Domain.Compute;
using TripPlanner.Infrastructure.Queue;
using MatchEntry = TripPlanner.Domain.Compute.MatchEntry;

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
    public void JobMapper_DriverRoute_SerializesAllFields()
    {
        var jobId  = Guid.NewGuid();
        var driver = Guid.NewGuid();
        var job    = new DriverRouteComputeJob(
            JobId:    jobId,
            DriverId: driver,
            Payload:  new DriverRouteJobPayload(new GeoPoint(52.2, 21.0), new GeoPoint(52.3, 21.1)),
            CreatedAt: DateTimeOffset.UtcNow,
            RetryCount: 2);

        var bytes = _jobMapper.ToDto(job);
        var msg   = ComputeMessage.Parser.ParseFrom(bytes.Span);

        Assert.Equal(jobId.ToString(),  msg.JobId);
        Assert.Equal("driver_route",    msg.JobType);
        Assert.Equal(driver.ToString(), msg.RequestingUserId);
        Assert.Equal(2,                 msg.RetryCount);
        Assert.Equal(52.2, msg.DriverRoute.Start.Lat, 6);
        Assert.Equal(21.0, msg.DriverRoute.Start.Lon, 6);
        Assert.Equal(52.3, msg.DriverRoute.End.Lat, 6);
        Assert.Equal(21.1, msg.DriverRoute.End.Lon, 6);
    }

    [Fact]
    public void JobMapper_PassengerMatch_SerializesAllFields()
    {
        var jobId     = Guid.NewGuid();
        var passenger = Guid.NewGuid();
        var job       = new PassengerMatchComputeJob(
            JobId:       jobId,
            PassengerId: passenger,
            Payload:     new PassengerMatchJobPayload(
                new GeoPoint(52.2, 21.0),
                new GeoPoint(52.3, 21.1),
                new MatchConstraints(MaxDetourKm: 3.5, MaxResults: 8)),
            CreatedAt: DateTimeOffset.UtcNow);

        var bytes = _jobMapper.ToDto(job);
        var msg   = ComputeMessage.Parser.ParseFrom(bytes.Span);

        Assert.Equal(jobId.ToString(),     msg.JobId);
        Assert.Equal("passenger_match",    msg.JobType);
        Assert.Equal(passenger.ToString(), msg.RequestingUserId);
        Assert.Equal(52.2, msg.PassengerMatch.Start.Lat, 6);
        Assert.Equal(21.1, msg.PassengerMatch.End.Lon, 6);
        Assert.Equal(3.5,  msg.PassengerMatch.Constraints.MaxDetourKm, 6);
        Assert.Equal(8,    msg.PassengerMatch.Constraints.MaxResults);
    }

    // ─── ComputeResultProtoMapper (route-calc → trip-planner) ────────────────

    [Fact]
    public void ResultMapper_DriverRoute_DeserializesAllFields()
    {
        var jobId = Guid.NewGuid();
        var proto = new ResultMessage
        {
            JobId   = jobId.ToString(),
            JobType = "driver_route",
            Success = true,
            DriverRoute = new DriverRouteResult
            {
                RouteGeomJson   = "{\"type\":\"LineString\"}",
                EtaSeconds      = 900,
                DistanceMeters  = 15000,
            },
        };

        var result = _resultMapper.ToDomain(proto.ToByteArray());

        var dr = Assert.IsType<DriverRouteComputeResult>(result);
        Assert.Equal(jobId,                    dr.JobId);
        Assert.Equal("{\"type\":\"LineString\"}", dr.Result.RouteGeomJson);
        Assert.Equal(900,                      dr.Result.EtaSeconds);
        Assert.Equal(15000,                    dr.Result.DistanceMeters);
    }

    [Fact]
    public void ResultMapper_PassengerMatch_DeserializesAllFields()
    {
        var jobId    = Guid.NewGuid();
        var routeId  = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var proto    = new ResultMessage
        {
            JobId   = jobId.ToString(),
            JobType = "passenger_match",
            Success = true,
            PassengerMatch = new PassengerMatchResult
            {
                Matches =
                {
                    new Core.V1.MatchEntry
                    {
                        DriverRouteId          = routeId.ToString(),
                        DriverId               = driverId.ToString(),
                        EtaToPassengerSeconds  = 300,
                        DetourMeters           = 800,
                        Score                  = 0.92,
                    },
                },
            },
        };

        var result = _resultMapper.ToDomain(proto.ToByteArray());

        var pm = Assert.IsType<PassengerMatchComputeResult>(result);
        Assert.Equal(jobId, pm.JobId);
        Assert.Single(pm.Result.Matches);

        var match = pm.Result.Matches[0];
        Assert.Equal(routeId,  match.DriverRouteId);
        Assert.Equal(driverId, match.DriverId);
        Assert.Equal(300,      match.EtaToPassengerSeconds);
        Assert.Equal(800,      match.DetourMeters);
        Assert.Equal(0.92,     match.Score, 6);
    }

    [Fact]
    public void ResultMapper_Failed_ReturnsFailedResult()
    {
        var jobId = Guid.NewGuid();
        var proto = new ResultMessage
        {
            JobId   = jobId.ToString(),
            JobType = "driver_route",
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
