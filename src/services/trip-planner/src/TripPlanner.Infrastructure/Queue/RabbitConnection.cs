using RabbitMQ.Client;

namespace TripPlanner.Infrastructure.Queue;

public class RabbitConnection(IConnectionFactory factory) : IAsyncDisposable
{
    private IConnection? _connection;

    public async Task<IConnection> GetAsync(CancellationToken ct = default)
    {
        _connection ??= await factory.CreateConnectionAsync(ct);
        return _connection;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}