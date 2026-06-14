// osrm_client.cpp - OSRM HTTP client with real libcurl support
#include "osrm_client.hpp"
#include <curl/curl.h>
#include <sstream>
#include <cmath>
#include <cctype>
#include <stdexcept>

namespace {

// Callback for libcurl to write response data into a string
static size_t WriteCallback(void* contents, size_t size, size_t nmemb, std::string* userp) {
    userp->append((char*)contents, size * nmemb);
    return size * nmemb;
}

double extract_json_number(const std::string& json, const std::string& key) {
    size_t pos = json.find("\"" + key + "\"");
    if (pos == std::string::npos) return 0.0;
    size_t colon_pos = json.find(":", pos);
    if (colon_pos == std::string::npos) return 0.0;
    size_t start = colon_pos + 1;
    while (start < json.length() && (json[start] == ' ' || json[start] == '[')) start++;
    size_t end = start;
    while (end < json.length() && (std::isdigit(json[end]) || json[end] == '.' || json[end] == '-')) end++;
    if (start == end) return 0.0;
    try {
        return std::stod(json.substr(start, end - start));
    } catch (...) {
        return 0.0;
    }
}

size_t find_matching_delimiter(const std::string& json, size_t open_pos, char open_char, char close_char) {
    if (open_pos >= json.size() || json[open_pos] != open_char) {
        return std::string::npos;
    }

    int depth = 0;
    bool in_string = false;
    for (size_t i = open_pos; i < json.size(); ++i) {
        char c = json[i];
        if (in_string) {
            if (c == '\\' && i + 1 < json.size()) {
                ++i;
                continue;
            }
            if (c == '"') {
                in_string = false;
            }
            continue;
        }
        if (c == '"') {
            in_string = true;
            continue;
        }
        if (c == open_char) {
            ++depth;
        } else if (c == close_char) {
            --depth;
            if (depth == 0) {
                return i;
            }
        }
    }
    return std::string::npos;
}

// Extract GeoJSON LineString coordinates from an OSRM route object JSON fragment.
std::vector<Point> parse_geometry(const std::string& json) {
    std::vector<Point> points;

    size_t geom_start = json.find("\"geometry\"");
    size_t search_from = (geom_start != std::string::npos) ? geom_start : 0;
    size_t coords_start = json.find("\"coordinates\"", search_from);
    if (coords_start == std::string::npos) {
        return points;
    }

    size_t array_start = json.find('[', coords_start);
    if (array_start == std::string::npos) {
        return points;
    }

    size_t array_end = find_matching_delimiter(json, array_start, '[', ']');
    if (array_end == std::string::npos) {
        return points;
    }

    size_t search_pos = array_start + 1;
    while (search_pos < array_end) {
        size_t pair_start = json.find('[', search_pos);
        if (pair_start == std::string::npos || pair_start >= array_end) {
            break;
        }

        size_t pair_end = json.find(']', pair_start);
        if (pair_end == std::string::npos || pair_end > array_end) {
            break;
        }

        size_t comma = json.find(',', pair_start);
        if (comma == std::string::npos || comma > pair_end) {
            break;
        }

        try {
            double lon = std::stod(json.substr(pair_start + 1, comma - pair_start - 1));
            double lat = std::stod(json.substr(comma + 1, pair_end - comma - 1));
            points.push_back({lat, lon});
        } catch (...) {
            break;
        }

        search_pos = pair_end + 1;
    }

    return points;
}

std::vector<std::string> extract_route_objects(const std::string& response_data) {
    std::vector<std::string> route_objects;

    size_t routes_start = response_data.find("\"routes\"");
    if (routes_start == std::string::npos) {
        return route_objects;
    }

    size_t array_start = response_data.find('[', routes_start);
    if (array_start == std::string::npos) {
        return route_objects;
    }

    size_t array_end = find_matching_delimiter(response_data, array_start, '[', ']');
    if (array_end == std::string::npos) {
        return route_objects;
    }

    size_t pos = array_start + 1;
    while (pos < array_end) {
        while (pos < array_end && (response_data[pos] == ' ' || response_data[pos] == ',')) {
            ++pos;
        }
        if (pos >= array_end || response_data[pos] != '{') {
            break;
        }

        size_t obj_end = find_matching_delimiter(response_data, pos, '{', '}');
        if (obj_end == std::string::npos || obj_end > array_end) {
            break;
        }

        route_objects.push_back(response_data.substr(pos, obj_end - pos + 1));
        pos = obj_end + 1;
    }

    return route_objects;
}

}  // anonymous namespace

namespace osrm {

std::vector<Point> parse_geometry_from_json(const std::string& json) {
    return parse_geometry(json);
}

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
        url << osrm_url << "/route/v1/driving/"
            << start.lon << "," << start.lat << ";"
            << end.lon << "," << end.lat
            << "?overview=full&geometries=geojson";
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

        auto route_objects = extract_route_objects(response_data);
        if (route_objects.empty()) {
            response.error_message = "Could not parse routes array";
            curl_easy_cleanup(curl);
            return response;
        }

        const std::string& route_obj = route_objects.front();
        response.distance_meters = extract_json_number(route_obj, "distance");
        response.duration_seconds = extract_json_number(route_obj, "duration");
        response.waypoints = parse_geometry(route_obj);
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
        url << osrm_url << "/route/v1/driving/"
            << start.lon << "," << start.lat << ";"
            << end.lon << "," << end.lat
            << "?alternatives=true&overview=full&geometries=geojson";
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

        auto route_objects = extract_route_objects(response_data);
        int route_count = 0;
        for (const auto& route_obj : route_objects) {
            if (route_count >= num_alternatives) {
                break;
            }

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
        }

        if (routes.empty()) {
            RouteResponse error_response;
            error_response.success = false;
            error_response.error_message = "Could not parse routes array";
            routes.push_back(error_response);
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
