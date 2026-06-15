using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TripPlanner.Domain.Compute;
using TripPlanner.Domain.Events;
using TripPlanner.Infrastructure.Kafka;
using TripPlanner.IntegrationTests.Fixtures;

namespace TripPlanner.IntegrationTests.Kafka;

[Collection("Kafka")]
public class EventPublisherTests(KafkaFixture kafka)
{
    private EventPublisher CreatePublisher() => new(Options.Create(new KafkaOptions
    {
        BootstrapServers = kafka.BootstrapServers,
        ConsumerGroupId  = "test-group",
    }), NullLogger<EventPublisher>.Instance);

    private async Task EnsureTopicAsync(string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = kafka.BootstrapServers,
        }).Build();

        try
        {
            await admin.CreateTopicsAsync([new TopicSpecification
            {
                Name              = topic,
                NumPartitions     = 1,
                ReplicationFactor = 1,
            }]);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r =>
            r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
            // idempotent
        }
    }

    private IConsumer<Ignore, string> CreateConsumer() =>
        new ConsumerBuilder<Ignore, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId          = $"test-{Guid.NewGuid()}",
            AutoOffsetReset  = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();

    // ─── notification-triggers ────────────────────────────────────────────────

    [Fact]
    public async Task Publish_RideRequestedEvent_DeliversEnvelopeToTopic()
    {
        var topic = $"test-ride-requested-{Guid.NewGuid():N}";
        await EnsureTopicAsync(topic);

        using var publisher = CreatePublisher();
        var requestId   = Guid.NewGuid();
        var routeId     = Guid.NewGuid();
        var driverId    = Guid.NewGuid();
        var passengerId = Guid.NewGuid();
        var @event      = new RideRequestedEvent(
            requestId, routeId, driverId, passengerId,
            new GeoPoint(52.2, 21.0), new GeoPoint(52.3, 21.1));

        await publisher.PublishAsync(topic, @event, default);

        using var consumer = CreateConsumer();
        consumer.Assign(new TopicPartitionOffset(topic, 0, Offset.Beginning));
        var record = consumer.Consume(TimeSpan.FromSeconds(15));

        Assert.NotNull(record?.Message.Value);

        // EventPublisher uses default JsonSerializer — properties are PascalCase.
        var envelope = JsonSerializer.Deserialize<JsonElement>(record.Message.Value);
        Assert.Equal("ride_requested", envelope.GetProperty("EventType").GetString());

        var payload = envelope.GetProperty("Payload");
        Assert.Equal(requestId.ToString(),   payload.GetProperty("RequestId").GetString());
        Assert.Equal(driverId.ToString(),    payload.GetProperty("DriverId").GetString());
        Assert.Equal(passengerId.ToString(), payload.GetProperty("PassengerId").GetString());
    }

    // ─── ride-completions ─────────────────────────────────────────────────────

    [Fact]
    public async Task Publish_RideCompletedEvent_DeliversEnvelopeToTopic()
    {
        var topic = $"test-ride-completed-{Guid.NewGuid():N}";
        await EnsureTopicAsync(topic);

        using var publisher = CreatePublisher();
        var rideId      = Guid.NewGuid();
        var driverId    = Guid.NewGuid();
        var passengerId = Guid.NewGuid();
        var frozenId    = Guid.NewGuid();
        var @event      = new RideCompletedEvent(rideId, driverId, passengerId, frozenId, 22.50m, DateTimeOffset.UtcNow);

        await publisher.PublishAsync(topic, @event, default);

        using var consumer = CreateConsumer();
        consumer.Assign(new TopicPartitionOffset(topic, 0, Offset.Beginning));
        var record = consumer.Consume(TimeSpan.FromSeconds(15));

        Assert.NotNull(record?.Message.Value);
        var envelope = JsonSerializer.Deserialize<JsonElement>(record.Message.Value);
        Assert.Equal("ride_completed", envelope.GetProperty("EventType").GetString());

        var payload = envelope.GetProperty("Payload");
        Assert.Equal(rideId.ToString(), payload.GetProperty("RideId").GetString());
        Assert.Equal(22.50m,            payload.GetProperty("Price").GetDecimal());
    }

    // ─── envelope structure ───────────────────────────────────────────────────

    [Fact]
    public async Task Publish_AlwaysWrapsPayload_InEnvelopeWithMetadata()
    {
        var topic = $"test-envelope-{Guid.NewGuid():N}";
        await EnsureTopicAsync(topic);

        using var publisher = CreatePublisher();
        var before = DateTimeOffset.UtcNow;

        await publisher.PublishAsync(topic,
            new RideRejectedEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            default);

        using var consumer = CreateConsumer();
        consumer.Assign(new TopicPartitionOffset(topic, 0, Offset.Beginning));
        var record = consumer.Consume(TimeSpan.FromSeconds(15));

        Assert.NotNull(record?.Message.Value);
        var envelope = JsonSerializer.Deserialize<JsonElement>(record.Message.Value);

        Assert.True(Guid.TryParse(envelope.GetProperty("EventId").GetString(), out _));
        var occurredAt = envelope.GetProperty("OccurredAt").GetDateTimeOffset();
        Assert.True(occurredAt >= before);
    }
}
