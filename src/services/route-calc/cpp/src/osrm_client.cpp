#include "osrm_client.hpp"
#include <sstream>
#include <cmath>
#include <stdexcept>

namespace {

constexpr double EARTH_RADIUS_KM = 6371.0;
constexpr double PI = 3.14159265358979323846;

double to_radians(double degrees) { 
    return degrees * PI / 180.0; 
}

// Forward declare distance_km helper from mock_alg
double distance_km_helper(double lat1, double lon1, double lat2, double lon2) {
    double lat1_rad = to_radians(lat1);
    double lat2_rad = to_radians(lat2);
    double delta_lat = to_radians(lat2 - lat1);
    double delta_lon = to_radians(lon2 - lon1);
    
    double a = std::sin(delta_lat / 2.0) * std::sin(delta_lat / 2.0) +
               std::cos(lat1_rad) * std::cos(lat2_rad) *
               std::sin(delta_lon / 2.0) * std::sin(delta_lon / 2.0);
    
    double c = 2.0 * std::atan2(std::sqrt(a), std::sqrt(1.0 - a));
    return EARTH_RADIUS_KM * c;
}

}

namespace osrm {

// Mock implementations - always used since CURL is typically not available
RouteResponse get_best_route(const Point& start, const Point& end, const std::string& osrm_url) {
    RouteResponse response;
    
    // Mock response when CURL is not available
    response.error_message = "Using mock response (libcurl not available)";
    response.waypoints = {start, end};
    
    // Estimate distance: great-circle distance * 1.2 (for road detours)
    double direct_km = distance_km_helper(start.lat, start.lon, end.lat, end.lon);
    response.distance_meters = direct_km * 1000.0 * 1.2;
    response.duration_seconds = response.distance_meters / 25.0;  // ~25 m/s average
    response.success = true;
    
    return response;
}

std::vector<RouteResponse> get_alternative_routes(const Point& start, const Point& end, int num_alternatives, const std::string& osrm_url) {
    std::vector<RouteResponse> routes;
    
    // Return multiple slightly different mock routes
    for (int i = 0; i < num_alternatives; ++i) {
        RouteResponse route = get_best_route(start, end, osrm_url);
        
        // Add slight variance
        route.distance_meters *= (1.0 + i * 0.1);
        route.duration_seconds = route.distance_meters / 25.0;
        
        routes.push_back(route);
    }
    
    return routes;
}

std::vector<ClosestRouteInfo> get_closest_routes(const Point& point, int num_routes, const std::string& osrm_url) {
    std::vector<ClosestRouteInfo> result;
    
    // Simulate finding closest routes by generating nearby points
    const double lat_offset = 0.05;    // ~5.5 km at equator
    const double lon_offset = 0.05;
    
    for (int i = 0; i < num_routes; ++i) {
        // Generate destination points around the reference point
        double angle = (i * 360.0 / num_routes) * (PI / 180.0);  // Convert to radians
        double distance_km = 5.0 + (i % 3) * 2.5;  // 5, 7.5, 10 km
        
        // Approximate coordinate offset (simplified)
        double offset_lat = std::sin(angle) * (distance_km / 111.0);
        double offset_lon = std::cos(angle) * (distance_km / 111.0);
        
        Point destination{point.lat + offset_lat, point.lon + offset_lon};
        
        // Get route from point to this simulated destination
        RouteResponse route_resp = get_best_route(point, destination, osrm_url);
        
        if (route_resp.success) {
            ClosestRouteInfo info;
            info.route_id = "route_" + std::to_string(i);
            info.waypoints = route_resp.waypoints;
            info.distance_to_point_meters = distance_km * 1000.0;  // Convert km to meters
            info.access_point = route_resp.waypoints.front();  // First waypoint is access point
            info.total_distance_meters = route_resp.distance_meters;
            info.total_duration_seconds = route_resp.duration_seconds;
            result.push_back(info);
        }
    }
    
    return result;
}

}  // namespace osrm
