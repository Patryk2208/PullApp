namespace TripPlanner.Api;

public static class HttpUtils
{
    public static Guid GetDriverId(HttpContext http) =>
        Guid.TryParse(http.Request.Headers["X-User-Id"].FirstOrDefault(), out var id)
            ? id
            : throw new UnauthorizedAccessException("missing X-User-Id header");
    
    public static Guid GetPassengerId(HttpContext http) =>
        Guid.TryParse(http.Request.Headers["X-User-Id"].FirstOrDefault(), out var id)
            ? id
            : throw new UnauthorizedAccessException("missing X-User-Id header");
}