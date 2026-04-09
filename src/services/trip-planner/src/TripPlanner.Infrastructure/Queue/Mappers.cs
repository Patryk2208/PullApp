using Core.V1;
using Google.Protobuf;
using TripPlanner.Domain;

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
        var parsed = ComputeMessage.Parser.ParseFrom(payload.Span);
        throw new NotImplementedException();
        return new ComputePayload
        {
            
        };
    }

    public ReadOnlyMemory<byte> ToDto(ComputePayload domain)
    {
        throw new NotImplementedException();
        return new ComputeMessage
        {

        }.ToByteArray();
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