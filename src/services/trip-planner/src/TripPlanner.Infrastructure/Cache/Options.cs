namespace TripPlanner.Infrastructure.Cache;

public class RedisOptions
{
    // Connection settings
    public required string ConnectionString { get; set; }
    public required string Password { get; set; }
    public bool UseSsl { get; set; } = false;
    public int ConnectTimeoutSeconds { get; set; } = 5;
    public int DefaultDatabase { get; set; } = 0;
    
    // Application-level defaults
    public required string KeyPrefix { get; set; }
    public TimeSpan DefaultCacheTtl { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan SlidingExpirationWindow { get; set; } = TimeSpan.FromMinutes(10);
    
    // Feature flags
    public bool EnableCompression { get; set; } = false;
    public bool EnableTracing { get; set; } = true;
    
    // Per-domain TTL overrides
    public Dictionary<string, TimeSpan> DomainTtls { get; set; } = new();
    
    // Retry policy
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);
    
    // Health check settings
    public bool EnableHealthChecks { get; set; } = true;
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);
}