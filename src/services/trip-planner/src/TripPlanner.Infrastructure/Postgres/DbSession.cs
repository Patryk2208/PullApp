using Npgsql;
using TripPlanner.Application.Repositories;

namespace TripPlanner.Infrastructure.Postgres;

// Scoped per-request. Holds a single open connection (lazy) and an optional
// transaction so multiple repository calls in one handler share the same
// connection and can be wrapped in a single atomic commit.
public sealed class DbSession(NpgsqlDataSource dataSource) : IUnitOfWork
{
    private NpgsqlConnection?  _conn;
    private NpgsqlTransaction? _tx;

    public async ValueTask<NpgsqlConnection> ConnAsync(CancellationToken ct)
    {
        if (_conn is null)
            _conn = await dataSource.OpenConnectionAsync(ct);
        return _conn;
    }

    public async ValueTask<NpgsqlCommand> CreateCommandAsync(CancellationToken ct)
    {
        var conn = await ConnAsync(ct);
        var cmd  = conn.CreateCommand();
        if (_tx is not null)
            cmd.Transaction = _tx;
        return cmd;
    }

    // Call in handlers that need atomicity across multiple repo calls.
    public async Task BeginAsync(CancellationToken ct)
        => _tx = await (await ConnAsync(ct)).BeginTransactionAsync(ct);

    public Task CommitAsync(CancellationToken ct)
        => _tx?.CommitAsync(ct) ?? Task.CompletedTask;

    public Task RollbackAsync(CancellationToken ct)
        => _tx?.RollbackAsync(ct) ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_tx   is not null) await _tx.DisposeAsync();
        if (_conn is not null) await _conn.DisposeAsync();
    }
}
