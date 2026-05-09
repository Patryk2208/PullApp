namespace TripPlanner.Application.Features.DTO;

// Shared across all endpoints.

public record GeoPointDto(double Lat, double Lng);

public record ErrorResponseDto(string Error, string Message, object? Details = null);
