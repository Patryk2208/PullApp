namespace TripPlanner.Infrastructure.Kafka;

public class KafkaOptions
{
    public required string BootstrapServers { get; set; }
    public string? SaslUsername { get; set; }
    public string? SaslPassword { get; set; }

    // Consumer settings
    public required string ConsumerGroupId { get; set; }
    public string AutoOffsetReset { get; set; } = "latest";

    // Topics this service consumes (from DriverTracker).
    public string DriverEventsTopic { get; set; } = "driver-events";
}
