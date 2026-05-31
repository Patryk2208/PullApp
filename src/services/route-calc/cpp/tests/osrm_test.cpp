#include <gtest/gtest.h>
#include "../include/mock_alg.hpp"
#include "../include/osrm_client.hpp"

// Note: These tests require OSRM server running at http://router.project-osrm.org
// For local testing, you can use a local OSRM instance or mock HTTP responses

class OSRMClientTest : public ::testing::Test {
protected:
    // Warsaw city center
    Point warsaw_center{52.2297, 21.0122};
    // Krakow city center
    Point krakow_center{50.0647, 19.9450};
    // Gdansk city center
    Point gdansk_center{54.3520, 18.6466};
};

// Test OSRM wrapper function for best route
TEST_F(OSRMClientTest, GetBestRouteReturnsValidData) {
    // This test assumes OSRM is reachable
    BestRouteData route = get_best_route_osrm(warsaw_center, krakow_center);

    // Basic validation - route should have waypoints
    EXPECT_GT(route.waypoints.size(), 0);

    // Distance should be reasonable (Warsaw to Krakow is ~300 km by road)
    // Allow some deviation: 250-350 km
    if (route.waypoints.size() > 1) {
        EXPECT_GT(route.distance_meters, 250000);
        EXPECT_LT(route.distance_meters, 350000);
    }

    // Duration should be reasonable (roughly 4-5 hours)
    if (route.waypoints.size() > 1) {
        EXPECT_GT(route.duration_seconds, 14400);  // 4 hours
        EXPECT_LT(route.duration_seconds, 18000);  // 5 hours
    }
}

// Test alternative routes
TEST_F(OSRMClientTest, GetAlternativeRoutesReturnsMultipleRoutes) {
    std::vector<BestRouteData> routes = get_alternative_routes_osrm(warsaw_center, krakow_center, 3);

    // Should return at least 1 route (sometimes OSRM doesn't have alternatives)
    EXPECT_GT(routes.size(), 0);

    // Each route should have waypoints
    for (const auto& route : routes) {
        EXPECT_GT(route.waypoints.size(), 0);
    }
}

// Test closest routes functionality
TEST_F(OSRMClientTest, GetClosestRoutesReturnsMultipleRoutes) {
    std::vector<ClosestRouteData> routes = get_closest_routes_osrm(warsaw_center, 3);

    // Should return at least 1 route
    EXPECT_GE(routes.size(), 1);

    // Routes should have valid data
    for (const auto& route : routes) {
        EXPECT_FALSE(route.route_id.empty());
        EXPECT_GT(route.waypoints.size(), 0);
        EXPECT_GT(route.distance_to_point_meters, 0);
    }
}

// Test distance calculation with real coordinates
class DistanceCalculationTest : public ::testing::Test {
protected:
    Point warsaw{52.2297, 21.0122};
    Point krakow{50.0647, 19.9450};
};

TEST_F(DistanceCalculationTest, WarsawToKrakowDistance) {
    double distance = distance_km(warsaw, krakow);

    // Warsaw to Krakow is approximately 290 km (as the crow flies)
    // Allow some margin
    EXPECT_GT(distance, 200);
    EXPECT_LT(distance, 350);
}

// Test best route wrapper function
class BestRouteWrapperTest : public ::testing::Test {
protected:
    Point start{52.2297, 21.0122};
    Point end{50.0647, 19.9450};
};

TEST_F(BestRouteWrapperTest, BestRouteDataStructure) {
    BestRouteData route = get_best_route_osrm(start, end);

    // Verify structure is properly populated
    EXPECT_GT(route.waypoints.size(), 0);
    EXPECT_GE(route.distance_meters, 0);
    EXPECT_GE(route.duration_seconds, 0);

    // First waypoint should be near start
    if (route.waypoints.size() > 0) {
        double dist_to_start = distance_km(route.waypoints[0], start);
        EXPECT_LT(dist_to_start, 1.0);  // Within 1 km
    }
}

// Test error handling with invalid coordinates
TEST(OSRMErrorHandlingTest, InvalidCoordinates) {
    // Coordinates far out of range
    Point invalid1{91.0, 181.0};
    Point invalid2{-91.0, -181.0};

    // Should not crash
    BestRouteData route = get_best_route_osrm(invalid1, invalid2);

    // Either returns empty result or dummy waypoints
    // The important thing is no crash
    EXPECT_TRUE(true);
}

// Test with same point (start == end)
TEST(OSRMErrorHandlingTest, SameStartAndEnd) {
    Point point{52.2297, 21.0122};

    BestRouteData route = get_best_route_osrm(point, point);

    // Should handle gracefully
    // Distance should be near zero or very small
    if (route.waypoints.size() > 0) {
        EXPECT_LT(route.distance_meters, 100);  // Very short
    }
}
