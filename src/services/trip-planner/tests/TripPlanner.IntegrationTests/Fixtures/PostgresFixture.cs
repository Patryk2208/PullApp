using System.Collections.Concurrent;
using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using TripPlanner.Infrastructure.Postgres;

namespace TripPlanner.IntegrationTests.Fixtures;

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture> { }

public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgis/postgis:16-3.4")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
        .Build();

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    // Tests create sessions via NewSession() but don't dispose them. Track and
    // dispose them between tests (in CleanAsync) so their pooled connections are
    // returned — otherwise the connection pool (max 100) exhausts across the suite.
    private readonly ConcurrentBag<DbSession> _sessions = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        DataSource = new NpgsqlDataSourceBuilder(_container.GetConnectionString()).Build();

        var init = new DatabaseInitializer(DataSource, NullLogger<DatabaseInitializer>.Instance);
        await init.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await DisposeSessionsAsync();
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    public DbSession NewSession()
    {
        var session = new DbSession(DataSource);
        _sessions.Add(session);
        return session;
    }

    private async Task DisposeSessionsAsync()
    {
        while (_sessions.TryTake(out var session))
            await session.DisposeAsync();
    }

    public async Task CleanAsync()
    {
        // Release the previous test's sessions first — disposing rolls back any
        // open transaction (freeing row locks) and returns connections to the pool
        // before we TRUNCATE.
        await DisposeSessionsAsync();

        await using var conn = await DataSource.OpenConnectionAsync();
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE routes, ride_requests, rides, route_jobs, service_areas";
        await cmd.ExecuteNonQueryAsync();
    }
}
