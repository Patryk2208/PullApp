namespace TripPlanner.Infrastructure.Queue;

public class RabbitMqOptions
{
    public required string Host { get; set; }
    public required int Port { get; set; }

    public required string Username { get; set; }
    public required string Password { get; set; }

    public required string Compute { get; set; }
    public required string Results { get; set; }

    public string Vhost          { get; set; } = "/";
    public int    MaxRetries     { get; set; } = 3;
    public int    PrefetchCount  { get; set; } = 4;
    public int    TimeoutSeconds { get; set; } = 10;
}