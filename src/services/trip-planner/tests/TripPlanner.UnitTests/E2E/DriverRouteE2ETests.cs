using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace TripPlanner.UnitTests.E2E
{
    public class DriverRouteE2ETests
    {
        private readonly HttpClient _client;

        public DriverRouteE2ETests()
        {
            _client = new HttpClient
            {
                BaseAddress = new Uri("http://localhost:5238")
            };
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
                    lng = 21.0500
                }
            };

            var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/driver/route");

            httpRequest.Headers.Add(
                "X-User-Id",
                "11111111-1111-1111-1111-111111111111");

            httpRequest.Content =
                JsonContent.Create(request);

            // Act

            var response =
                await _client.SendAsync(httpRequest);

            // Assert

            Assert.Equal(
                HttpStatusCode.Accepted,
                response.StatusCode);
        }

        [Fact]
        public async Task DriverRouteFlow_ShouldReceiveSseEvent()
        {
            // Arrange

            var driverId =
                "11111111-1111-1111-1111-111111111111";

            var jobId =
                Guid.NewGuid();

            using var cts =
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(10));

            // ─────────────────────────────────────────────────────────────
            // OPEN SSE CONNECTION
            // ─────────────────────────────────────────────────────────────

            var sseRequest =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    $"/api/driver/route/{jobId}/events");

            sseRequest.Headers.Add(
                "X-User-Id",
                driverId);

            sseRequest.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "text/event-stream"));

            var sseResponse =
                await _client.SendAsync(
                    sseRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    cts.Token);

            sseResponse.EnsureSuccessStatusCode();

            // ─────────────────────────────────────────────────────────────
            // START READING SSE STREAM
            // ─────────────────────────────────────────────────────────────

            var stream =
                await sseResponse.Content.ReadAsStreamAsync(
                    cts.Token);

            using var reader =
                new StreamReader(stream);

            // ─────────────────────────────────────────────────────────────
            // SEND ROUTE REQUEST
            // ─────────────────────────────────────────────────────────────

            var routeRequest = new
            {
                start = new
                {
                    lat = 52.2297,
                    lng = 21.0122
                },
                end = new
                {
                    lat = 52.2400,
                    lng = 21.0500
                }
            };

            var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    "/api/driver/route");

            request.Headers.Add(
                "X-User-Id",
                driverId);

            request.Content =
                JsonContent.Create(routeRequest);

            var response =
                await _client.SendAsync(
                    request,
                    cts.Token);

            response.EnsureSuccessStatusCode();

            // ─────────────────────────────────────────────────────────────
            // WAIT FOR SSE EVENT
            // ─────────────────────────────────────────────────────────────

            var lines = new List<string>();

            while (!reader.EndOfStream)
            {
                var line =
                    await reader.ReadLineAsync(cts.Token);

                if (line is null)
                    continue;

                lines.Add(line);

                if (line.Contains("drier_route_computed"))
                    break;
            }

            var fullResponse =
                string.Join("\n", lines);

            // Assert

            Assert.Contains(
                "drier_route_computed",
                fullResponse);
        }

        // --------------------------------------------------------------------------

        //[Fact]
        //public async Task DriverRouteFlow_ShouldNotReceiveEvent_ForDifferentDriver()
        //{
        //    // Arrange

        //    var listeningDriverId =
        //        "11111111-1111-1111-1111-111111111111";

        //    var publishingDriverId =
        //        "22222222-2222-2222-2222-222222222222";

        //    var fakeJobId =
        //        Guid.NewGuid();

        //    using var cts =
        //        new CancellationTokenSource();

        //    // ─────────────────────────────────────────────────────────────
        //    // OPEN SSE CONNECTION FOR DRIVER A
        //    // ─────────────────────────────────────────────────────────────

        //    var sseRequest =
        //        new HttpRequestMessage(
        //            HttpMethod.Get,
        //            $"/api/driver/route/{fakeJobId}/events");

        //    sseRequest.Headers.Add(
        //        "X-User-Id",
        //        listeningDriverId);

        //    var sseResponse =
        //        await _client.SendAsync(
        //            sseRequest,
        //            HttpCompletionOption.ResponseHeadersRead,
        //            cts.Token);

        //    sseResponse.EnsureSuccessStatusCode();

        //    var stream =
        //        await sseResponse.Content.ReadAsStreamAsync(
        //            cts.Token);

        //    using var reader =
        //        new StreamReader(stream);

        //    // ─────────────────────────────────────────────────────────────
        //    // DRIVER B SENDS ROUTE
        //    // ─────────────────────────────────────────────────────────────

        //    var routeRequest = new
        //    {
        //        start = new
        //        {
        //            lat = 52.2297,
        //            lng = 21.0122
        //        },
        //        end = new
        //        {
        //            lat = 52.2400,
        //            lng = 21.0500
        //        }
        //    };

        //    var request =
        //        new HttpRequestMessage(
        //            HttpMethod.Post,
        //            "/api/driver/route");

        //    request.Headers.Add(
        //        "X-User-Id",
        //        publishingDriverId);

        //    request.Content =
        //        JsonContent.Create(routeRequest);

        //    var response =
        //        await _client.SendAsync(request);

        //    response.EnsureSuccessStatusCode();

        //    // ─────────────────────────────────────────────────────────────
        //    // LISTEN FOR SSE EVENT
        //    // ─────────────────────────────────────────────────────────────

        //    var readTask = Task.Run(async () =>
        //    {
        //        while (true)
        //        {
        //            var line =
        //                await reader.ReadLineAsync();

        //            if (line is null)
        //                continue;

        //            if (line.Contains(
        //                "drier_route_computed"))
        //            {
        //                return true;
        //            }
        //        }
        //    });

        //    var timeoutTask =
        //        Task.Delay(3000);

        //    var completedTask =
        //        await Task.WhenAny(
        //            readTask,
        //            timeoutTask);

        //    // CLOSE SSE CONNECTION

        //    cts.Cancel();

        //    var receivedAnyEvent =
        //        completedTask == readTask;

        //    // Assert

        //    Assert.False(receivedAnyEvent);
        //}
    }
}
