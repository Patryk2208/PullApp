using Core.V1;
using Google.Protobuf;
using TripPlanner.Domain.Compute;
using ProtoPoint = Core.V1.Point;

namespace TripPlanner.Infrastructure.Queue;

public interface IQueueDomainMapper<out T>
{
    T ToDomain(in ReadOnlyMemory<byte> payload);
}

public interface IQueueDtoMapper<in T>
{
    ReadOnlyMemory<byte> ToDto(T domain);
}

// ─── ComputeJob: Trip Planner → Route-Calc ───────────────────────────────────

public class ComputeJobProtoMapper : IQueueDtoMapper<ComputeJob>
{
    public ReadOnlyMemory<byte> ToDto(ComputeJob domain)
    {
        var msg = new ComputeMessage
        {
            JobId            = domain.JobId.ToString(),
            Algorithm        = domain.JobType == JobType.BestRoute ? "best_route" : "ride_matching",
            RequestingUserId = domain.RequestingUserId.ToString(),
            CreatedAt        = domain.CreatedAt.ToUnixTimeMilliseconds(),
            RetryCount       = domain.RetryCount,
        };

        switch (domain)
        {
            case BestRouteComputeJob brj:
                msg.BestRoute = new BestRouteParams
                {
                    Start    = ToProtoPoint(brj.Payload.Start),
                    End      = ToProtoPoint(brj.Payload.End),
                    CostType = brj.Payload.CostType,
                };
                break;

            case RideMatchingComputeJob rmj:
                msg.RideMatching = new RideMatchingQuery
                {
                    PassengerId      = rmj.PassengerId.ToString(),
                    Start            = ToProtoPoint(rmj.Payload.Start),
                    End              = ToProtoPoint(rmj.Payload.End),
                    DepartureDate    = rmj.Payload.DepartureDate,
                    SeatsNeeded      = rmj.Payload.SeatsNeeded,
                    MaxDetourKm      = rmj.Payload.MaxDetourKm,
                    TimeWindowMinutes = rmj.Payload.TimeWindowMinutes,
                };
                break;
        }

        return msg.ToByteArray();
    }

    private static ProtoPoint ToProtoPoint(GeoPoint p) => new() { Lat = p.Latitude, Lon = p.Longitude };
}

// ─── ComputeJobResult: Route-Calc → Trip Planner ─────────────────────────────

public class ComputeResultProtoMapper : IQueueDomainMapper<ComputeJobResult>
{
    public ComputeJobResult ToDomain(in ReadOnlyMemory<byte> payload)
    {
        var msg = ResultMessage.Parser.ParseFrom(payload.Span);

        var jobId = Guid.Parse(msg.JobId);

        if (!msg.Success)
        {
            var failedJobType = msg.ResultCase == ResultMessage.ResultOneofCase.RideMatching
                ? JobType.RideMatching
                : JobType.BestRoute;
            return new FailedComputeResult(jobId, failedJobType, msg.Error);
        }

        return msg.ResultCase switch
        {
            ResultMessage.ResultOneofCase.BestRoute => new BestRouteComputeResult(
                jobId,
                new BestRouteJobResult(
                    msg.BestRoute.Points.Select(p => new GeoPoint(p.Lat, p.Lon)).ToList(),
                    msg.BestRoute.DistanceMeters,
                    msg.BestRoute.DurationSeconds)),

            ResultMessage.ResultOneofCase.RideMatching => new RideMatchingComputeResult(
                jobId,
                new RideMatchingJobResult(
                    msg.RideMatching.Matches
                       .Select(m => new MatchEntry(
                           m.RouteId,
                           m.DriverId,
                           m.MatchScore,
                           m.DetourKm,
                           m.PickupPointIndex,
                           m.DropoffPointIndex))
                       .ToList())),

            _ => new FailedComputeResult(jobId, JobType.BestRoute, "unknown_result_type"),
        };
    }
}