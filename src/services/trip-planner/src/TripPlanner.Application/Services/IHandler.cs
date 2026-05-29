namespace TripPlanner.Application.Services;

public interface IHandler<in T>
{
    Task HandleAsync(T message, CancellationToken ct);
}
