#include "../include/osrm_client.hpp"
#include <cassert>
#include <iostream>
#include <string>

int main() {
    const std::string sample = R"({
        "distance": 12345.6,
        "duration": 789.0,
        "geometry": {
            "type": "LineString",
            "coordinates": [
                [21.0122, 52.2297],
                [21.0130, 52.2300],
                [19.9450, 50.0647]
            ]
        }
    })";

    auto points = osrm::parse_geometry_from_json(sample);
    assert(points.size() == 3);
    assert(std::abs(points[0].lat - 52.2297) < 1e-6);
    assert(std::abs(points[0].lon - 21.0122) < 1e-6);
    assert(std::abs(points[2].lat - 50.0647) < 1e-6);
    assert(std::abs(points[2].lon - 19.9450) < 1e-6);

    const std::string encoded_polyline_only = R"({"geometry":"obx}Hm}f_CAEYBS"})";
    auto empty = osrm::parse_geometry_from_json(encoded_polyline_only);
    assert(empty.empty());

    std::cout << "geometry_parse_test passed\n";
    return 0;
}
