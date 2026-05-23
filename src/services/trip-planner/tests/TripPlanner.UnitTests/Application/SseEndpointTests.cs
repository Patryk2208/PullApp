using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
using TripPlanner.Infrastructure.Sse;

namespace TripPlanner.UnitTests.Application
{
    public class SseEndpointTests
    : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public SseEndpointTests(
            WebApplicationFactory<Program> factory)
        {
            _factory = factory;

            _client = factory.CreateClient();
        }

        [Fact]
        public async Task PushAsync_ShouldDeliverMessage_ToRegisteredChannel()
        {
            // Arrange

            var hub = new InMemorySseHub();

            var userId = Guid.NewGuid();

            var channel = hub.Register(userId);

            // Act

            await hub.PushAsync(
                userId,
                "ride_started",
                """{"rideId":"123"}""",
                CancellationToken.None);

            var message = await channel.Reader.ReadAsync();

            // Assert

            Assert.Equal("ride_started", message.EventType);

            Assert.Equal(
                """{"rideId":"123"}""",
                message.Json);

            Assert.False(message.Close);
        }

        [Fact]
        public async Task CloseAsync_ShouldSendCloseMessage_AndCompleteChannel()
        {
            // Arrange

            var hub = new InMemorySseHub();

            var userId = Guid.NewGuid();

            var channel = hub.Register(userId);

            // Act

            await hub.CloseAsync(userId, CancellationToken.None);

            var message = await channel.Reader.ReadAsync();

            // Assert

            Assert.Equal("close", message.EventType);

            Assert.True(message.Close);
        }
    }
}
