namespace TripPlanner.Application.Services;

public class KafkaTopics
{
    public string NotificationTriggers { get; set; } = "notification-triggers";
    public string RideCompletions      { get; set; } = "ride-completions";
}
