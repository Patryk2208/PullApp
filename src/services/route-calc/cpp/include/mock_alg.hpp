#pragma once

#include <vector>
#include <cmath>
#include <string>

// Represents a geographic point (latitude, longitude)
struct Point {
    double lat;
    double lon;
};

// Represents a potential match between passenger and driver
struct RideMatch {
    std::string route_id;
    std::string driver_id;
    double match_score;  // 0.0 to 1.0
    double detour_km;
    int pickup_index;
    int dropoff_index;
};

// Calculate great-circle distance between two points in kilometers
double distance_km(const Point& p1, const Point& p2);

// Find the closest point on a driver's route to a passenger's point
// Returns: (closest_point_index, distance_to_closest_km)
struct ClosestPointResult {
    int index;
    double distance_km;
};
ClosestPointResult find_closest_point_on_route(
    const Point& passenger_point,
    const std::vector<Point>& driver_route
);

// Match a passenger's ride request to a driver's posted route
// Returns match score (0.0-1.0) or 0.0 if no good match
// Sets pickup_index and dropoff_index if match found
RideMatch match_single_route(
    const Point& passenger_start,
    const Point& passenger_end,
    const std::string& route_id,
    const std::string& driver_id,
    const std::vector<Point>& driver_route,
    double max_detour_km = 10.0
);

std::vector<double> slow_algorithm(double input, int seconds);