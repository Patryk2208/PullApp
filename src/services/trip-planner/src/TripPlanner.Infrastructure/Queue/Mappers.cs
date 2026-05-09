using Core.V1;
using Google.Protobuf;
using TripPlanner.Domain.Compute;
using ClosestRoutesParams = TripPlanner.Domain.Compute.ClosestRoutesParams;
using ProtoClosestRoutesParams = Core.V1.ClosestRoutesParams;
using BestRouteParams = TripPlanner.Domain.Compute.BestRouteParams;
using ClosestRoutesResult = TripPlanner.Domain.Compute.ClosestRoutesResult;
using Point = Core.V1.Point;
using ProtoBestRouteParams = Core.V1.BestRouteParams;

namespace TripPlanner.Infrastructure.Queue;

internal interface IQueueDomainMapper<out T>
{
    T ToDomain(in ReadOnlyMemory<byte> payload);
}

internal interface IQueueDtoMapper<in T>
{
    ReadOnlyMemory<byte> ToDto(T domain);
} 


internal class ComputeProtoQueueMapper : IQueueDomainMapper<ComputePayload>, IQueueDtoMapper<ComputePayload>
{
    public ComputePayload ToDomain(in ReadOnlyMemory<byte> payload)
    {
        throw new NotImplementedException();
    }

    public ReadOnlyMemory<byte> ToDto(ComputePayload domain)
    {
        var alg = domain.Algorithm switch
        {
            AlgorithmType.BestRoute => "best_route",
            AlgorithmType.ClosestRoutes => "closest_routes",
            _ => throw new ArgumentOutOfRangeException()
        };
        var msg = new ComputeMessage
        {
            JobId = domain.Id.ToString(),
            Algorithm = alg,
            CreatedAt = domain.RequestedAt.Ticks,
            RetryCount = 0

        };
        switch (domain.Algorithm)
        {
            case AlgorithmType.BestRoute:
                msg.BestRoute = new ProtoBestRouteParams
                {
                    Start = new Point
                    {
                        Lat = ((BestRouteParams)domain.Params).From.Latitude,
                        Lon = ((BestRouteParams)domain.Params).From.Longitude
                    },
                    End = new Point
                    {
                        Lat = ((BestRouteParams)domain.Params).To.Latitude,
                        Lon = ((BestRouteParams)domain.Params).To.Longitude
                    },
                    CostType = ((BestRouteParams)domain.Params).Criteria switch
                    {
                        _ => "distance"
                    },
                };
                break;
            case AlgorithmType.ClosestRoutes:
                msg.ClosestRoutes = new ProtoClosestRoutesParams
                {
                    Point = new Point
                    {
                        Lat = ((ClosestRoutesParams)domain.Params).P.Latitude,
                        Lon = ((ClosestRoutesParams)domain.Params).P.Longitude
                    },
                    K = 0, //todo
                    RadiusMeters = ((ClosestRoutesParams)domain.Params).Radius,
                };
                break;
        }
        return msg.ToByteArray();
    }
}

internal class ResultProtoQueueMapper : IQueueDomainMapper<ComputeResult>, IQueueDtoMapper<ComputeResult>
{
    public ComputeResult ToDomain(in ReadOnlyMemory<byte> payload)
    {
        throw new NotImplementedException();
    }

    public ReadOnlyMemory<byte> ToDto(ComputeResult domain)
    {
        throw new NotImplementedException();
    }
}