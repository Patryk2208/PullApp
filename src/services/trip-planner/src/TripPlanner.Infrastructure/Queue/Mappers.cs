using Core.V1;
using Google.Protobuf;
using TripPlanner.Domain.Compute;
using MatchEntry = TripPlanner.Domain.Compute.MatchEntry;
using ProtoPoint = Core.V1.Point;

namespace TripPlanner.Infrastructure.Queue;

internal interface IQueueDomainMapper<out T>
{
    T ToDomain(in ReadOnlyMemory<byte> payload);
}

internal interface IQueueDtoMapper<in T>
{
    ReadOnlyMemory<byte> ToDto(T domain);
}

// ─── ComputeJob: Trip Planner → Route-Calc ───────────────────────────────────

internal class ComputeJobProtoMapper : IQueueDtoMapper<ComputeJob>
{
    public ReadOnlyMemory<byte> ToDto(ComputeJob domain)
    {
        var msg = new ComputeMessage
        {
            JobId       = domain.JobId.ToString(),
            JobType     = domain.JobType == JobType.DriverRoute ? "driver_route" : "passenger_match",
            RequestingUserId = domain.RequestingUserId.ToString(),
            CreatedAt   = domain.CreatedAt.ToUnixTimeMilliseconds(),
            RetryCount  = domain.RetryCount,
        };

        switch (domain)
        {
            case DriverRouteComputeJob drj:
                msg.DriverRoute = new DriverRouteParams
                {
                    Start = ToProtoPoint(drj.Payload.Start),
                    End   = ToProtoPoint(drj.Payload.End),
                };
                break;

            case PassengerMatchComputeJob pmj:
                msg.PassengerMatch = new PassengerMatchParams
                {
                    Start = ToProtoPoint(pmj.Payload.Start),
                    End   = ToProtoPoint(pmj.Payload.End),
                    Constraints = new PassengerMatchConstraints
                    {
                        MaxDetourKm = pmj.Payload.Constraints.MaxDetourKm,
                        MaxResults  = pmj.Payload.Constraints.MaxResults,
                    },
                };
                break;
        }

        return msg.ToByteArray();
    }

    private static ProtoPoint ToProtoPoint(GeoPoint p) => new() { Lat = p.Latitude, Lon = p.Longitude };
}

// ─── ComputeJobResult: Route-Calc → Trip Planner ─────────────────────────────

internal class ComputeResultProtoMapper : IQueueDomainMapper<ComputeJobResult>
{
    public ComputeJobResult ToDomain(in ReadOnlyMemory<byte> payload)
    {
        var msg = ResultMessage.Parser.ParseFrom(payload.Span);

        var jobId   = Guid.Parse(msg.JobId);
        var jobType = msg.JobType == "driver_route" ? JobType.DriverRoute : JobType.PassengerMatch;

        if (!msg.Success)
            return new FailedComputeResult(jobId, jobType, msg.Error);

        return msg.ResultCase switch
        {
            ResultMessage.ResultOneofCase.DriverRoute => new DriverRouteComputeResult(
                jobId,
                new DriverRouteJobResult(
                    msg.DriverRoute.RouteGeomJson,
                    msg.DriverRoute.EtaSeconds,
                    msg.DriverRoute.DistanceMeters)),

            ResultMessage.ResultOneofCase.PassengerMatch => new PassengerMatchComputeResult(
                jobId,
                new PassengerMatchJobResult(
                    msg.PassengerMatch.Matches
                       .Select(m => new MatchEntry(
                           Guid.Parse(m.DriverRouteId),
                           Guid.Parse(m.DriverId),
                           m.EtaToPassengerSeconds,
                           m.DetourMeters,
                           m.Score))
                       .ToList())),

            _ => new FailedComputeResult(jobId, jobType, "unknown_result_type"),
        };
    }
}
