namespace TripPlanner.Domain.Compute;

public class ComputePayload
{
    public Guid Id { get; set; }
    public AlgorithmType Algorithm { get; set; }
    public DateTime RequestedAt { get; set; }
    public AlgorithmParams Params { get; set; }
}

public class ComputeResult
{
    public Guid Id { get; set; }
    public ComputeDetails Details { get; set; }
    public AlgorithmResults Results { get; set; }
}

public class ComputeDetails
{
    public DateTime RequestedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? Duration { get; set; }
    public bool Success { get; set; }
    public Exception? Exception { get; set; }
}