namespace TripPlanner.Domain.Compute;

public enum JobType { DriverRoute, PassengerMatch }

// ─── What Trip Planner enqueues ───────────────────────────────────────────────

public abstract record ComputeJob(
    Guid JobId,
    JobType JobType,
    Guid RequestingUserId,
    DateTimeOffset CreatedAt,
    int RetryCount = 0);

public record DriverRouteComputeJob(
    Guid JobId,
    Guid DriverId,
    DriverRouteJobPayload Payload,
    DateTimeOffset CreatedAt,
    int RetryCount = 0)
    : ComputeJob(JobId, JobType.DriverRoute, DriverId, CreatedAt, RetryCount);

public record PassengerMatchComputeJob(
    Guid JobId,
    Guid PassengerId,
    PassengerMatchJobPayload Payload,
    DateTimeOffset CreatedAt,
    int RetryCount = 0)
    : ComputeJob(JobId, JobType.PassengerMatch, PassengerId, CreatedAt, RetryCount);

// ─── What Trip Planner dequeues ───────────────────────────────────────────────

public abstract record ComputeJobResult(Guid JobId, JobType JobType, bool Success, string? Error);

public record DriverRouteComputeResult(Guid JobId, DriverRouteJobResult Result)
    : ComputeJobResult(JobId, JobType.DriverRoute, true, null);

public record PassengerMatchComputeResult(Guid JobId, PassengerMatchJobResult Result)
    : ComputeJobResult(JobId, JobType.PassengerMatch, true, null);

public record FailedComputeResult(Guid JobId, JobType JobType, string Error)
    : ComputeJobResult(JobId, JobType, false, Error);
