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

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        DataSource = new NpgsqlDataSourceBuilder(_container.GetConnectionString()).Build();

        var init = new DatabaseInitializer(DataSource, NullLogger<DatabaseInitializer>.Instance);
        await init.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    public DbSession NewSession() => new(DataSource);

    public async Task CleanAsync()
    {
        await using var conn = await DataSource.OpenConnectionAsync();
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE routes, ride_requests, rides, route_jobs, service_areas";
        await cmd.ExecuteNonQueryAsync();
    }
}
