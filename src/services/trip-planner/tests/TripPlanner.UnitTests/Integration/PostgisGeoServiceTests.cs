
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Text;
using TripPlanner.Domain.Compute;
using TripPlanner.Infrastructure.Postgres;

namespace TripPlanner.UnitTests.Integration
{
    // Integration tests for PostgisGeoService. These tests require a running Postgres
    // instance with the PostGIS extension and the appropriate schema.
    //
    // Main purpose is to verify that the service correctly determines whether a given 
    // geographic point is within the defined service areas in the database.
    public class PostgisGeoServiceTests
    {
        private readonly NpgsqlDataSource _dataSource;

        public PostgisGeoServiceTests()
        {
            var cs = Environment.GetEnvironmentVariable("TRIP_PLANNER_TEST_DB");
            _dataSource = new NpgsqlDataSourceBuilder(cs).Build();
        }

        [Fact]
        public async Task IsWithinServiceAreaAsync_ShouldReturnTrue()
        {
            // Arrange

            var initializer = new DatabaseInitializer(
                _dataSource,
                NullLogger<DatabaseInitializer>.Instance);

            var seeder = new ServiceAreaSeeder(
                _dataSource,
                NullLogger<ServiceAreaSeeder>.Instance);

            await initializer.StartAsync(CancellationToken.None);

            await seeder.StartAsync(CancellationToken.None);

            var dbSession = new DbSession(_dataSource);

            var geoService = new PostgisGeoService(dbSession);

            var warsaw = new GeoPoint(
                52.2297,
                21.0122);

            // Act

            var result = await geoService.IsWithinServiceAreaAsync(
                warsaw,
                CancellationToken.None);

            // Assert

            Assert.True(result);
        }

        [Fact]
        public async Task IsWithinServiceAreaAsync_ShouldReturnFalse()
        {
            // Arrange

            var initializer = new DatabaseInitializer(
                _dataSource,
                NullLogger<DatabaseInitializer>.Instance);

            var seeder = new ServiceAreaSeeder(
                _dataSource,
                NullLogger<ServiceAreaSeeder>.Instance);

            await initializer.StartAsync(CancellationToken.None);

            await seeder.StartAsync(CancellationToken.None);

            var dbSession = new DbSession(_dataSource);

            var geoService = new PostgisGeoService(dbSession);

            var poznan = new GeoPoint(
                52.4064,
                16.9252);

            // Act

            var result = await geoService.IsWithinServiceAreaAsync(
                poznan,
                CancellationToken.None);

            // Assert

            Assert.False(result);
        }
    }
}
