// osrm_client.cpp - OSRM HTTP client with real libcurl support
#include "osrm_client.hpp"
#include <curl/curl.h>
#include <sstream>
#include <cmath>
#include <stdexcept>

namespace {

// Callback for libcurl to write response data into a string
static size_t WriteCallback(void* contents, size_t size, size_t nmemb, std::string* userp) {
    userp->append((char*)contents, size * nmemb);
    return size * nmemb;
}

// Simple JSON parsing without external dependency (for OSRM response)
std::string extract_json_string(const std::string& json, const std::string& key) {
    size_t pos = json.find("\"" + key + "\"");
    if (pos == std::string::npos) return "";
    size_t colon_pos = json.find(":", pos);
    if (colon_pos == std::string::npos) return "";
    size_t quote_start = json.find("\"", colon_pos);
    if (quote_start == std::string::npos) return "";
    size_t quote_end = json.find("\"", quote_start + 1);
    if (quote_end == std::string::npos) return "";
    return json.substr(quote_start + 1, quote_end - quote_start - 1);
}

double extract_json_number(const std::string& json, const std::string& key) {
    size_t pos = json.find("\"" + key + "\"");
    if (pos == std::string::npos) return 0.0;
    size_t colon_pos = json.find(":", pos);
    if (colon_pos == std::string::npos) return 0.0;
    size_t start = colon_pos + 1;
    while (start < json.length() && (json[start] == ' ' || json[start] == '[')) start++;
    size_t end = start;
    while (end < json.length() && (std::isdigit(json[end]) || json[end] == '.')) end++;
    if (start == end) return 0.0;
    try {
        return std::stod(json.substr(start, end - start));
    } catch (...) {
        return 0.0;
    }
}

// Extract waypoints from OSRM geometry
std::vector<Point> parse_geometry(const std::string& json) {
    std::vector<Point> points;
    size_t coords_start = json.find("\"coordinates\"");
    if (coords_start == std::string::npos) return points;
    size_t array_start = json.find("[", coords_start);
    if (array_start == std::string::npos) return points;
    size_t search_pos = array_start;
    int waypoint_count = 0;
    while (search_pos < json.length() && waypoint_count < 100) {
        size_t pair_start = json.find("[", search_pos);
        if (pair_start == std::string::npos || pair_start > json.find("]", search_pos)) break;
        size_t first_comma = json.find(",", pair_start);
        size_t pair_end = json.find("]", first_comma);
        if (first_comma == std::string::npos || pair_end == std::string::npos) break;
        try {
            double lon = std::stod(json.substr(pair_start + 1, first_comma - pair_start - 1));
            double lat = std::stod(json.substr(first_comma + 1, pair_end - first_comma - 1));
            points.push_back({lat, lon});
            waypoint_count++;
            search_pos = pair_end + 1;
        } catch (...) {
            break;
        }
    }
    return points;
}

}  // anonymous namespace

namespace osrm {

RouteResponse get_best_route(const Point& start, const Point& end, const std::string& osrm_url) {
    RouteResponse response;
    response.success = false;
    try {
        CURL* curl = curl_easy_init();
        if (!curl) {
            response.error_message = "Failed to initialize CURL";
            return response;
        }
        std::ostringstream url;
        url << osrm_url << "/route/v1/driving/" << start.lon << "," << start.lat << ";" << end.lon << "," << end.lat << "?overview=full";
        std::string response_data;
        curl_easy_setopt(curl, CURLOPT_URL, url.str().c_str());
        curl_easy_setopt(curl, CURLOPT_WRITEFUNCTION, WriteCallback);
        curl_easy_setopt(curl, CURLOPT_WRITEDATA, &response_data);
        curl_easy_setopt(curl, CURLOPT_TIMEOUT, 30L);
        CURLcode res = curl_easy_perform(curl);
        if (res != CURLE_OK) {
            response.error_message = "CURL error: " + std::string(curl_easy_strerror(res));
            curl_easy_cleanup(curl);
            return response;
        }
        if (response_data.find("\"routes\"") == std::string::npos || response_data.find("\"code\":\"Ok\"") == std::string::npos) {
            response.error_message = "No valid route found or API error";
            curl_easy_cleanup(curl);
            return response;
        }
        response.distance_meters = extract_json_number(response_data, "distance");
        response.duration_seconds = extract_json_number(response_data, "duration");
        response.waypoints = parse_geometry(response_data);
        if (response.waypoints.empty()) {
            response.waypoints = {start, end};
        }
        response.success = true;
        curl_easy_cleanup(curl);
    } catch (const std::exception& e) {
        response.error_message = "Exception: " + std::string(e.what());
    }
    return response;
}

std::vector<RouteResponse> get_alternative_routes(const Point& start, const Point& end, int num_alternatives, const std::string& osrm_url) {
    std::vector<RouteResponse> routes;
    try {
        CURL* curl = curl_easy_init();
        if (!curl) {
            RouteResponse error_response;
            error_response.success = false;
            error_response.error_message = "Failed to initialize CURL";
            routes.push_back(error_response);
            return routes;
        }
        std::ostringstream url;
        url << osrm_url << "/route/v1/driving/" << start.lon << "," << start.lat << ";" << end.lon << "," << end.lat << "?alternatives=true&overview=full";
        std::string response_data;
        curl_easy_setopt(curl, CURLOPT_URL, url.str().c_str());
        curl_easy_setopt(curl, CURLOPT_WRITEFUNCTION, WriteCallback);
        curl_easy_setopt(curl, CURLOPT_WRITEDATA, &response_data);
        curl_easy_setopt(curl, CURLOPT_TIMEOUT, 30L);
        CURLcode res = curl_easy_perform(curl);
        if (res != CURLE_OK) {
            RouteResponse error_response;
            error_response.success = false;
            error_response.error_message = "CURL error: " + std::string(curl_easy_strerror(res));
            routes.push_back(error_response);
            curl_easy_cleanup(curl);
            return routes;
        }
        if (response_data.find("\"routes\"") == std::string::npos) {
            RouteResponse error_response;
            error_response.success = false;
            error_response.error_message = "No routes found in response";
            routes.push_back(error_response);
            curl_easy_cleanup(curl);
            return routes;
        }
        size_t route_start = response_data.find("\"routes\":");
        if (route_start == std::string::npos) {
            RouteResponse error_response;
            error_response.success = false;
            error_response.error_message = "Could not find routes in response";
            routes.push_back(error_response);
            curl_easy_cleanup(curl);
            return routes;
        }
        size_t array_start = response_data.find("[", route_start);
        size_t array_end = response_data.find("]", array_start);
        if (array_start == std::string::npos || array_end == std::string::npos) {
            RouteResponse error_response;
            error_response.success = false;
            error_response.error_message = "Could not parse routes array";
            routes.push_back(error_response);
            curl_easy_cleanup(curl);
            return routes;
        }
        std::string routes_section = response_data.substr(array_start, array_end - array_start + 1);
        size_t pos = 1;
        int route_count = 0;
        while (pos < routes_section.length() && route_count < num_alternatives) {
            size_t obj_start = routes_section.find("{", pos);
            size_t obj_end = routes_section.find("}", obj_start);
            if (obj_start == std::string::npos || obj_end == std::string::npos) break;
            std::string route_obj = routes_section.substr(obj_start, obj_end - obj_start + 1);
            RouteResponse route;
            route.distance_meters = extract_json_number(route_obj, "distance");
            route.duration_seconds = extract_json_number(route_obj, "duration");
            route.waypoints = parse_geometry(route_obj);
            if (route.waypoints.empty()) {
                route.waypoints = {start, end};
            }
            route.success = true;
            routes.push_back(route);
            route_count++;
            pos = obj_end + 1;
        }
        curl_easy_cleanup(curl);
    } catch (const std::exception& e) {
        RouteResponse error_response;
        error_response.success = false;
        error_response.error_message = "Exception: " + std::string(e.what());
        routes.push_back(error_response);
    }
    return routes;
}

std::vector<ClosestRouteInfo> get_closest_routes(const Point& point, int num_routes, const std::string& osrm_url) {
    std::vector<ClosestRouteInfo> result;
    for (int i = 0; i < num_routes; ++i) {
        double angle = (i * 360.0 / num_routes) * (3.14159 / 180.0);
        double distance_km = 5.0 + (i % 3) * 2.5;
        double offset_lat = std::sin(angle) * (distance_km / 111.0);
        double offset_lon = std::cos(angle) * (distance_km / 111.0);
        Point destination{point.lat + offset_lat, point.lon + offset_lon};
        RouteResponse route_resp = get_best_route(point, destination, osrm_url);
        if (route_resp.success) {
            ClosestRouteInfo info;
            info.route_id = "route_" + std::to_string(i);
            info.waypoints = route_resp.waypoints;
            info.distance_to_point_meters = distance_km * 1000.0;
            info.access_point = route_resp.waypoints.front();
            info.total_distance_meters = route_resp.distance_meters;
            info.total_duration_seconds = route_resp.duration_seconds;
            result.push_back(info);
        }
    }
    return result;
}

}  // namespace osrm

