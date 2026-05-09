using TripPlanner.Application.Repositories;

namespace TripPlanner.Infrastructure.Fakes;

// No-op subscriber used when RabbitMQ is not configured (dev / unit tests).
// In production, RabbitSubscriber<ComputeJobResult> is wired up instead.
public class FakeSubscriber<T> : ISubscriber
{
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public void Dispose() { }
}
