using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TripPlanner.Domain.Compute;
using TripPlanner.Infrastructure.Queue;
using TripPlanner.IntegrationTests.Fixtures;
using Core.V1;

namespace TripPlanner.IntegrationTests.Queue;

[Collection("RabbitMq")]
public class RabbitPublisherTests(RabbitMqFixture rabbit)
{
    private const string TestQueue = "test-compute-jobs";

    private (RabbitComputePublisher<ComputeJob>, IConnectionFactory) CreatePublisher()
    {
        var factory = rabbit.CreateConnectionFactory();
        var options = Options.Create(new RabbitMqOptions
        {
            Host          = rabbit.Host,
            Port          = rabbit.Port,
            Username      = rabbit.Username,
            Password      = rabbit.Password,
            Compute       = TestQueue,
            Results       = "test-compute-results",
        });
        var connection = new RabbitConnection(factory);
        var publisher  = new RabbitComputePublisher<ComputeJob>(connection, new ComputeJobProtoMapper(), options);
        return (publisher, factory);
    }

    private static async Task<BasicGetResult> PollQueueAsync(
        IChannel channel, string queue, CancellationToken ct, int maxAttempts = 20)
    {
        for (var i = 0; i < maxAttempts; i++)
        {
            var msg = await channel.BasicGetAsync(queue, autoAck: true, ct);
            if (msg is not null) return msg;
            await Task.Delay(100, ct);
        }
        throw new TimeoutException($"No message arrived in queue '{queue}' within timeout.");
    }

    // ─── DriverRoute ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Publish_DriverRouteJob_MessageArrivesInQueue()
    {
        var (publisher, factory) = CreatePublisher();
        var jobId  = Guid.NewGuid();
        var driver = Guid.NewGuid();
        var job    = new DriverRouteComputeJob(
            JobId:    jobId,
            DriverId: driver,
            Payload:  new DriverRouteJobPayload(new GeoPoint(52.2, 21.0), new GeoPoint(52.3, 21.1)),
            CreatedAt: DateTimeOffset.UtcNow);

        await publisher.PublishAsync(job, default);

        // Consume raw bytes directly.
        var conn    = await factory.CreateConnectionAsync();
        var channel = await conn.CreateChannelAsync(new CreateChannelOptions(false, false));
        await channel.QueueDeclareAsync(TestQueue, true, false, false);

        var raw = await PollQueueAsync(channel, TestQueue, default);
        var msg = ComputeMessage.Parser.ParseFrom(raw.Body.Span);

        Assert.Equal(jobId.ToString(),  msg.JobId);
        Assert.Equal("driver_route",    msg.JobType);
        Assert.Equal(driver.ToString(), msg.RequestingUserId);
        Assert.Equal(52.2, msg.DriverRoute.Start.Lat, 6);
        Assert.Equal(52.3, msg.DriverRoute.End.Lat, 6);
    }

    // ─── PassengerMatch ───────────────────────────────────────────────────────

    [Fact]
    public async Task Publish_PassengerMatchJob_MessageArrivesInQueue()
    {
        var (publisher, factory) = CreatePublisher();
        var jobId     = Guid.NewGuid();
        var passenger = Guid.NewGuid();
        var job       = new PassengerMatchComputeJob(
            JobId:       jobId,
            PassengerId: passenger,
            Payload:     new PassengerMatchJobPayload(
                new GeoPoint(52.2, 21.0),
                new GeoPoint(52.3, 21.1),
                new MatchConstraints(MaxDetourKm: 4, MaxResults: 5)),
            CreatedAt: DateTimeOffset.UtcNow);

        await publisher.PublishAsync(job, default);

        var conn    = await factory.CreateConnectionAsync();
        var channel = await conn.CreateChannelAsync(new CreateChannelOptions(false, false));
        await channel.QueueDeclareAsync(TestQueue, true, false, false);

        var raw = await PollQueueAsync(channel, TestQueue, default);
        var msg = ComputeMessage.Parser.ParseFrom(raw.Body.Span);

        Assert.Equal(jobId.ToString(),     msg.JobId);
        Assert.Equal("passenger_match",    msg.JobType);
        Assert.Equal(passenger.ToString(), msg.RequestingUserId);
        Assert.Equal(4.0, msg.PassengerMatch.Constraints.MaxDetourKm, 6);
        Assert.Equal(5,   msg.PassengerMatch.Constraints.MaxResults);
    }
}
