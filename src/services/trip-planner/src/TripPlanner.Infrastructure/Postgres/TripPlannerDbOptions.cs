namespace TripPlanner.Infrastructure.Postgres;

public class TripPlannerDbOptions
{
    public string    Host     { get; set; } = "";
    public int       Port     { get; set; } = 5432;
    public string    Db       { get; set; } = "";
    public string    Username { get; set; } = "";
    public string    Password { get; set; } = "";
    public string    SslMode  { get; set; } = "disable";
    public PoolOptions Pool   { get; set; } = new();

    public string BuildConnectionString() =>
        $"Host={Host};Port={Port};Database={Db};Username={Username};Password={Password};" +
        $"SSL Mode={SslMode};Minimum Pool Size={Pool.MinConnections};Maximum Pool Size={Pool.MaxConnections}";
}

public class PoolOptions
{
    public int MinConnections { get; set; } = 1;
    public int MaxConnections { get; set; } = 10;
}