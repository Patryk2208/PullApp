#pragma once

#include <vector>
#include <string>
#include <optional>
#include "mock_alg.hpp"

namespace osrm {

struct RouteResponse {
    std::vector<Point> waypoints;
    double distance_meters;
    double duration_seconds;
    bool success;
    std::string error_message;
};

struct ClosestRouteInfo {
    std::string route_id;
    std::vector<Point> waypoints;
    double distance_to_point_meters;
    Point access_point;
    double total_distance_meters;
    double total_duration_seconds;
};

/**
 * Query OSRM for the best route between two points
 * @param start Starting point
 * @param end Ending point
 * @param osrm_url OSRM server URL (e.g., "http://router.project-osrm.org")
 * @return RouteResponse with waypoints and metrics, or error
 */
RouteResponse get_best_route(const Point& start, const Point& end, const std::string& osrm_url = "http://router.project-osrm.org");

/**
 * Query OSRM for alternative routes between two points
 * @param start Starting point
 * @param end Ending point
 * @param num_alternatives Number of alternative routes to request
 * @param osrm_url OSRM server URL
 * @return Vector of RouteResponse objects
 */
std::vector<RouteResponse> get_alternative_routes(const Point& start, const Point& end, int num_alternatives = 3, const std::string& osrm_url = "http://router.project-osrm.org");

/**
 * Find closest routes to a point (simulated by querying nearby coordinates)
 * @param point Reference point
 * @param num_routes Number of routes to simulate
 * @param osrm_url OSRM server URL
 * @return Vector of simulated closest routes with waypoints
 */
std::vector<ClosestRouteInfo> get_closest_routes(const Point& point, int num_routes = 3, const std::string& osrm_url = "http://router.project-osrm.org");

} // namespace osrm
