using Npgsql;
using TripPlanner.Domain.Compute;
using TripPlanner.Infrastructure.Postgres;
using TripPlanner.IntegrationTests.Fixtures;

namespace TripPlanner.IntegrationTests.Postgres;

[Collection("Postgres")]
public class GeoServiceTests(PostgresFixture db) : IAsyncLifetime
{
    // Warsaw bounding box (roughly)
    private const string WarsawPolygon =
        "POLYGON((20.8 51.9, 21.3 51.9, 21.3 52.4, 20.8 52.4, 20.8 51.9))";

    public async Task InitializeAsync()
    {
        await db.CleanAsync();
        await SeedServiceAreaAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedServiceAreaAsync()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO service_areas (name, area, is_active)
            VALUES ('Warsaw', ST_GeogFromText('{WarsawPolygon}'), true)
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private PostgisGeoService Svc() => new(db.NewSession());

    // ─── IsWithinServiceArea ─────────────────────────────────────────────────

    [Fact]
    public async Task IsWithinServiceArea_ReturnsTrue_ForPointInsidePolygon()
    {
        var warsaw = new GeoPoint(52.23, 21.01); // city center
        var result = await Svc().IsWithinServiceAreaAsync(warsaw, default);
        Assert.True(result);
    }

    [Fact]
    public async Task IsWithinServiceArea_ReturnsFalse_ForPointOutsidePolygon()
    {
        var krakow = new GeoPoint(50.06, 19.94); // different city
        var result = await Svc().IsWithinServiceAreaAsync(krakow, default);
        Assert.False(result);
    }

    [Fact]
    public async Task IsWithinServiceArea_ReturnsFalse_WhenNoActiveServiceArea()
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = "UPDATE service_areas SET is_active = false";
        await cmd.ExecuteNonQueryAsync();

        var warsaw = new GeoPoint(52.23, 21.01);
        var result = await Svc().IsWithinServiceAreaAsync(warsaw, default);
        Assert.False(result);
    }

    // ─── IsNearAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task IsNear_ReturnsTrue_ForPointsWithin100m()
    {
        var a = new GeoPoint(52.2297, 21.0122); // Palace of Culture
        var b = new GeoPoint(52.2300, 21.0130); // ~50m away
        var result = await Svc().IsNearAsync(a, b, thresholdMeters: 200, default);
        Assert.True(result);
    }

    [Fact]
    public async Task IsNear_ReturnsFalse_ForDistantPoints()
    {
        var warsaw = new GeoPoint(52.23, 21.01);
        var krakow = new GeoPoint(50.06, 19.94);
        var result = await Svc().IsNearAsync(warsaw, krakow, thresholdMeters: 200, default);
        Assert.False(result);
    }

    [Fact]
    public async Task IsNear_ReturnsTrue_ForIdenticalPoints()
    {
        var a = new GeoPoint(52.23, 21.01);
        var result = await Svc().IsNearAsync(a, a, thresholdMeters: 1, default);
        Assert.True(result);
    }
}
