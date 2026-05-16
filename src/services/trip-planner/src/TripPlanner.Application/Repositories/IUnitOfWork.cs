namespace TripPlanner.Application.Repositories;

public interface IUnitOfWork : IAsyncDisposable
{
    Task BeginAsync(CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
    Task RollbackAsync(CancellationToken ct);
}
