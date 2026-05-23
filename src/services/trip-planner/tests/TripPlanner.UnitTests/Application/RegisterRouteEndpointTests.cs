using Microsoft.AspNetCore.Mvc.Testing;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace TripPlanner.UnitTests.Application
{
    public class RegisterRouteEndpointTests
    : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public RegisterRouteEndpointTests(
            WebApplicationFactory<Program> factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task RegisterRoute_ShouldReturnAccepted()
        {
            // Arrange

            var request = new
            {
                start = new
                {
                    lat = 52.2297,
                    lng = 21.0122
                },
                end = new
                {
                    lat = 52.2400,
                    lng = 21.0200
                }
            };

            var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/driver/route");

            httpRequest.Headers.Add(
                "X-User-Id",
                "11111111-1111-1111-1111-111111111111");

            httpRequest.Content = JsonContent.Create(request);

            // Act

            var response = await _client.SendAsync(httpRequest);

            // Assert

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        [Fact]
        public async Task RegisterRoute_ShouldFail_ForLocationOutsideServiceArea()
        {
            // Arrange

            var request = new
            {
                start = new
                {
                    lat = 52.4064,
                    lng = 16.9252
                },
                end = new
                {
                    lat = 52.5000,
                    lng = 16.9000
                }
            };

            var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/driver/route");

            httpRequest.Headers.Add(
                "X-User-Id",
                "11111111-1111-1111-1111-111111111111");

            httpRequest.Content = JsonContent.Create(request);

            // Act

            var response = await _client.SendAsync(httpRequest);

            // Assert

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        }

        [Fact]
        public async Task RegisterRoute_ShouldFail_WhenUserHeaderMissing()
        {
            // Arrange

            var request = new
            {
                start = new
                {
                    lat = 52.2297,
                    lng = 21.0122
                },
                end = new
                {
                    lat = 52.2400,
                    lng = 21.0200
                }
            };

            var response = await _client.PostAsJsonAsync(
                "/api/driver/route",
                request);

            // Assert

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task RegisterRoute_ShouldFail_ForInvalidRequest()
        {
            // Arrange

            var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/driver/route");

            httpRequest.Headers.Add(
                "X-User-Id",
                "11111111-1111-1111-1111-111111111111");

            httpRequest.Content = JsonContent.Create(new { });

            // Act

            var response = await _client.SendAsync(httpRequest);

            // Assert

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
