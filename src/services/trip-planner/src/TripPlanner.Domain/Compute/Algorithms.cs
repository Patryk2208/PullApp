using TripPlanner.Domain.Driver;

namespace TripPlanner.Domain.Compute;

public enum AlgorithmType
{
    BestRoute,
    ClosestRoutes,
    // todo rest
}

public abstract class AlgorithmParams {}

public abstract class AlgorithmResults {}


//BestRouteParams
public enum BestRouteAlgorithm 
{
    //todo
}

public enum BestRouteCriteria
{
    Distance,
    Time,
    Cost,
    //todo
}

public class BestRouteParams : AlgorithmParams
{
    public Point From { get; set; }
    public Point To { get; set; }
    public BestRouteCriteria Criteria { get; set; }
}

public class BestRouteResult : AlgorithmResults
{
    public Route Route { get; set; }
    public float Distance { get; set; }
    public float Time { get; set; }
    public BestRouteAlgorithm AlgorithmUsed { get; set; }
    
}

//ClosestRoutesParams
public class ClosestRoutesParams : AlgorithmParams
{
    public Point P { get; set; }
    public float Radius { get; set; }
}

public record CloseRoute(Trip Trip, float Distance, Point Intersection);

public class ClosestRoutesResult : AlgorithmResults
{
    public List<CloseRoute> CloseRoutes { get; set; }
}