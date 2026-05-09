using System.Collections.Concurrent;
using System.Threading.Channels;
using TripPlanner.Application.Services;

namespace TripPlanner.Infrastructure.Sse;

public record SseMessage(string EventType, string Json, bool Close = false);

// Thread-safe hub that holds one unbounded channel per active SSE connection.
// The SSE endpoint registers the channel; handlers push to it via ISseHub.
public class InMemorySseHub : ISseHub
{
    private readonly ConcurrentDictionary<Guid, Channel<SseMessage>> _channels = new();

    // Called by the SSE endpoint to create a connection slot.
    public Channel<SseMessage> Register(Guid key)
    {
        var channel = Channel.CreateUnbounded<SseMessage>(
            new UnboundedChannelOptions { SingleReader = true });
        _channels[key] = channel;
        return channel;
    }

    // Called by the SSE endpoint when the client disconnects.
    public void Unregister(Guid key) => _channels.TryRemove(key, out _);

    public async Task PushAsync(Guid key, string eventType, string jsonPayload, CancellationToken ct)
    {
        if (_channels.TryGetValue(key, out var channel))
            await channel.Writer.WriteAsync(new SseMessage(eventType, jsonPayload), ct);
    }

    public async Task CloseAsync(Guid key, CancellationToken ct)
    {
        if (_channels.TryRemove(key, out var channel))
        {
            await channel.Writer.WriteAsync(new SseMessage("close", "{}", Close: true), ct);
            channel.Writer.Complete();
        }
    }
}
