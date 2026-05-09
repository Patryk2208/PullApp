using TripPlanner.Domain.Compute;

namespace TripPlanner.Domain;

public enum JobStatus { Pending, Completed, Failed }

public class RouteJob
{
    public Guid Id { get; init; }

    // Used to correlate RabbitMQ reply messages back to this job.
    public Guid CorrelationId { get; init; }

    public JobType JobType { get; init; }

    // driver_id for DriverRoute jobs, passenger_id for PassengerMatch jobs.
    public Guid RequesterId { get; init; }

    public JobStatus Status { get; private set; } = JobStatus.Pending;

    // Serialised job payload stored for audit / replay.
    public string PayloadJson { get; init; } = default!;

    // Populated on completion — GeoJSON route for driver jobs, JSON match array for passenger jobs.
    public string? ResultJson { get; private set; }

    public string? ErrorReason { get; private set; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; private set; }

    // For passenger match jobs: match results expire after 10 minutes (see §4 Redis schema).
    public DateTimeOffset? ExpiresAt { get; init; }

    public void Complete(string resultJson)
    {
        Status = JobStatus.Completed;
        ResultJson = resultJson;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string reason)
    {
        Status = JobStatus.Failed;
        ErrorReason = reason;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
