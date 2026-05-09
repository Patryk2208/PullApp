using TripPlanner.Domain.Compute;

namespace TripPlanner.Domain.Driver;

// todo treat this as an aggregate???
public class Trip
{
    public Guid Id { get; set; }
    public Driver Driver { get; set; }
    
    public int MaxSeats { get; set; }
    public List<Passenger.Passenger> Passengers { get; set; }
    
    public Route Route { get; set; }
}