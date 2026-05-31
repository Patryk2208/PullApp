namespace TripPlanner.Domain.Compute;

public enum JobType { BestRoute, RideMatching }

// ─── What Trip Planner enqueues ───────────────────────────────────────────────

public abstract record ComputeJob(
    Guid JobId,
    JobType JobType,
    Guid RequestingUserId,
    DateTimeOffset CreatedAt,
    int RetryCount = 0);

public record BestRouteComputeJob(
    Guid JobId,
    Guid DriverId,
    BestRouteJobPayload Payload,
    DateTimeOffset CreatedAt,
    int RetryCount = 0)
    : ComputeJob(JobId, JobType.BestRoute, DriverId, CreatedAt, RetryCount);

public record RideMatchingComputeJob(
    Guid JobId,
    Guid PassengerId,
    RideMatchingJobPayload Payload,
    DateTimeOffset CreatedAt,
    int RetryCount = 0)
    : ComputeJob(JobId, JobType.RideMatching, PassengerId, CreatedAt, RetryCount);

// ─── What Trip Planner dequeues ───────────────────────────────────────────────

public abstract record ComputeJobResult(Guid JobId, JobType JobType, bool Success, string? Error);

public record BestRouteComputeResult(Guid JobId, BestRouteJobResult Result)
    : ComputeJobResult(JobId, JobType.BestRoute, true, null);

public record RideMatchingComputeResult(Guid JobId, RideMatchingJobResult Result)
    : ComputeJobResult(JobId, JobType.RideMatching, true, null);

public record FailedComputeResult(Guid JobId, JobType JobType, string Error)
    : ComputeJobResult(JobId, JobType, false, Error);