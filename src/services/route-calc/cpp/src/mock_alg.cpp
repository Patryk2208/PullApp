#include "../include/mock_alg.hpp"
#include <cmath>
#include <limits>
#include <algorithm>
#include <thread>
#include <chrono>
#include <vector>

constexpr double EARTH_RADIUS_KM = 6371.0;
constexpr double PI = 3.14159265358979323846;

double to_radians(double degrees) {
    return degrees * PI / 180.0;
}

double distance_km(const Point& p1, const Point& p2) {
    double lat1_rad = to_radians(p1.lat);
    double lat2_rad = to_radians(p2.lat);
    double delta_lat = to_radians(p2.lat - p1.lat);
    double delta_lon = to_radians(p2.lon - p1.lon);
    
    double a = std::sin(delta_lat / 2.0) * std::sin(delta_lat / 2.0) +
               std::cos(lat1_rad) * std::cos(lat2_rad) *
               std::sin(delta_lon / 2.0) * std::sin(delta_lon / 2.0);
    
    double c = 2.0 * std::atan2(std::sqrt(a), std::sqrt(1.0 - a));
    return EARTH_RADIUS_KM * c;
}

ClosestPointResult find_closest_point_on_route(
    const Point& target,
    const std::vector<Point>& route) {

    if (route.size() < 2) {
        return {-1, std::numeric_limits<double>::max()};
    }

    double min_distance = std::numeric_limits<double>::max();
    int closest_index = -1;

    for (size_t i = 0; i < route.size(); ++i) {
        double d = distance_km(target, route[i]);
        if (d < min_distance) {
            min_distance = d;
            closest_index = static_cast<int>(i);
        }
    }

    return {closest_index, min_distance};
}

RideMatch match_single_route(
    const Point& passenger_start,
    const Point& passenger_end,
    const std::string& route_id,
    const std::string& driver_id,
    const std::vector<Point>& driver_route,
    double max_detour_km) {
    
    if (driver_route.size() < 2) {
        return RideMatch{
            route_id,
            driver_id,
            0.0,
            0.0,
            -1,
            -1
        };
    }
    
    ClosestPointResult pickup_result = find_closest_point_on_route(passenger_start, driver_route);
    ClosestPointResult dropoff_result = find_closest_point_on_route(passenger_end, driver_route);

    int pickup_idx = pickup_result.index;
    int dropoff_idx = dropoff_result.index;
    
    if (pickup_idx < 0 || dropoff_idx < 0 || dropoff_idx < pickup_idx) {
        return RideMatch{
            route_id,
            driver_id,
            0.0,
            0.0,
            -1,
            -1
        };
    }
    
    double passenger_direct_distance = distance_km(passenger_start, passenger_end);
    
    double driver_segment_distance = 0.0;
    for (int i = pickup_idx; i < dropoff_idx; ++i) {
        driver_segment_distance += distance_km(driver_route[i], driver_route[i + 1]);
    }
    
    double pickup_detour = distance_km(passenger_start, driver_route[pickup_idx]);
    double dropoff_detour = distance_km(driver_route[dropoff_idx], passenger_end);
    
    double total_detour_km = pickup_detour + driver_segment_distance + dropoff_detour - passenger_direct_distance;
    
    if (total_detour_km > max_detour_km) {
        return RideMatch{
            route_id,
            driver_id,
            0.0,
            0.0,
            -1,
            -1
        };
    }
    
    double match_score = std::max(0.0, 1.0 - (total_detour_km / max_detour_km));
    
    return RideMatch{
        route_id,
        driver_id,
        match_score,
        total_detour_km,
        pickup_idx,
        dropoff_idx
    };
}

std::vector<double> slow_algorithm(double input, int seconds) {
    std::this_thread::sleep_for(std::chrono::seconds(seconds));
    return {input, input * 2};
}
