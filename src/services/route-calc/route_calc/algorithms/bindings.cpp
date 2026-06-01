#include <pybind11/pybind11.h>
#include <pybind11/stl.h>
#include "mock_alg.hpp"
#include "osrm_client.hpp"

namespace py = pybind11;

PYBIND11_MODULE(route_calc_algorithms_module, m) {
    m.doc() = "Mock routing algorithm for testing";

    // Bind Point struct
    py::class_<Point>(m, "Point")
        .def(py::init<>())
        .def(py::init<double, double>(), py::arg("lat"), py::arg("lon"))
        .def_readwrite("lat", &Point::lat)
        .def_readwrite("lon", &Point::lon);

    // Bind ClosestPointResult struct
    py::class_<ClosestPointResult>(m, "ClosestPointResult")
        .def_readwrite("index", &ClosestPointResult::index)
        .def_readwrite("distance_km", &ClosestPointResult::distance_km);

    // Bind RideMatch struct
    py::class_<RideMatch>(m, "RideMatch")
        .def(py::init<>())
        .def_readwrite("route_id", &RideMatch::route_id)
        .def_readwrite("driver_id", &RideMatch::driver_id)
        .def_readwrite("match_score", &RideMatch::match_score)
        .def_readwrite("detour_km", &RideMatch::detour_km)
        .def_readwrite("pickup_index", &RideMatch::pickup_index)
        .def_readwrite("dropoff_index", &RideMatch::dropoff_index);

    // Bind functions with explicit GIL release/reacquire (releases GIL during C++ execution)
    m.def("distance_km", [](const Point& p1, const Point& p2) {
        py::gil_scoped_release release;
        auto result = distance_km(p1, p2);
        py::gil_scoped_acquire acquire;
        return result;
    },
          py::arg("p1"), py::arg("p2"),
          "Calculate distance between two points in kilometers");

    m.def("find_closest_point_on_route", [](const Point& passenger_point, const std::vector<Point>& driver_route) {
        py::gil_scoped_release release;
        auto result = find_closest_point_on_route(passenger_point, driver_route);
        py::gil_scoped_acquire acquire;
        return result;
    },
          py::arg("passenger_point"), py::arg("driver_route"),
          "Find closest point on a route to a passenger's location");

    m.def("match_single_route", [](const Point& passenger_start, const Point& passenger_end,
                                   const std::string& route_id, const std::string& driver_id,
                                   const std::vector<Point>& driver_route, double max_detour_km) {
        py::gil_scoped_release release;
        auto result = match_single_route(passenger_start, passenger_end, route_id, driver_id, driver_route, max_detour_km);
        py::gil_scoped_acquire acquire;
        return result;
    },
          py::arg("passenger_start"), py::arg("passenger_end"),
          py::arg("route_id"), py::arg("driver_id"), py::arg("driver_route"),
          py::arg("max_detour_km") = 10.0,
          "Match a passenger's ride request to a driver's route");

    m.def("slow_algorithm", [](double input, int seconds) {
        py::gil_scoped_release release;
        slow_algorithm(input, seconds);
        py::gil_scoped_acquire acquire;
    },
          py::arg("input"), py::arg("seconds"),
          "Sleeps for N seconds (releases GIL)");

    // Bind OSRM RouteResponse struct
    py::class_<osrm::RouteResponse>(m, "OSRMRouteResponse")
        .def(py::init<>())
        .def_readwrite("waypoints", &osrm::RouteResponse::waypoints)
        .def_readwrite("distance_meters", &osrm::RouteResponse::distance_meters)
        .def_readwrite("duration_seconds", &osrm::RouteResponse::duration_seconds)
        .def_readwrite("success", &osrm::RouteResponse::success)
        .def_readwrite("error_message", &osrm::RouteResponse::error_message);

    // Bind OSRM ClosestRouteInfo struct
    py::class_<osrm::ClosestRouteInfo>(m, "OSRMClosestRouteInfo")
        .def(py::init<>())
        .def_readwrite("route_id", &osrm::ClosestRouteInfo::route_id)
        .def_readwrite("waypoints", &osrm::ClosestRouteInfo::waypoints)
        .def_readwrite("distance_to_point_meters", &osrm::ClosestRouteInfo::distance_to_point_meters)
        .def_readwrite("access_point", &osrm::ClosestRouteInfo::access_point)
        .def_readwrite("total_distance_meters", &osrm::ClosestRouteInfo::total_distance_meters)
        .def_readwrite("total_duration_seconds", &osrm::ClosestRouteInfo::total_duration_seconds);

    // Bind OSRM functions
    m.def("get_best_route", [](const Point& start, const Point& end, const std::string& osrm_url) {
        py::gil_scoped_release release;
        auto result = osrm::get_best_route(start, end, osrm_url);
        py::gil_scoped_acquire acquire;
        return result;
    },
          py::arg("start"), py::arg("end"), py::arg("osrm_url") = "http://router.project-osrm.org",
          "Query OSRM for the best route between two points");

    m.def("get_alternative_routes", [](const Point& start, const Point& end, int num_alternatives, const std::string& osrm_url) {
        py::gil_scoped_release release;
        auto result = osrm::get_alternative_routes(start, end, num_alternatives, osrm_url);
        py::gil_scoped_acquire acquire;
        return result;
    },
          py::arg("start"), py::arg("end"), py::arg("num_alternatives") = 3, py::arg("osrm_url") = "http://router.project-osrm.org",
          "Query OSRM for alternative routes between two points");

    m.def("get_closest_routes", [](const Point& point, int num_routes, const std::string& osrm_url) {
        py::gil_scoped_release release;
        auto result = osrm::get_closest_routes(point, num_routes, osrm_url);
        py::gil_scoped_acquire acquire;
        return result;
    },
          py::arg("point"), py::arg("num_routes") = 3, py::arg("osrm_url") = "http://router.project-osrm.org",
          "Find closest routes to a point");

    // Bind BestRouteData struct
    py::class_<BestRouteData>(m, "BestRouteData")
        .def(py::init<>())
        .def_readwrite("waypoints", &BestRouteData::waypoints)
        .def_readwrite("distance_meters", &BestRouteData::distance_meters)
        .def_readwrite("duration_seconds", &BestRouteData::duration_seconds);

    // Bind ClosestRouteData struct
    py::class_<ClosestRouteData>(m, "ClosestRouteData")
        .def(py::init<>())
        .def_readwrite("route_id", &ClosestRouteData::route_id)
        .def_readwrite("waypoints", &ClosestRouteData::waypoints)
        .def_readwrite("distance_to_point_meters", &ClosestRouteData::distance_to_point_meters)
        .def_readwrite("access_point", &ClosestRouteData::access_point)
        .def_readwrite("total_distance_meters", &ClosestRouteData::total_distance_meters)
        .def_readwrite("total_duration_seconds", &ClosestRouteData::total_duration_seconds);

    // Bind wrapper functions
    m.def("get_best_route_osrm", [](const Point& start, const Point& end, const std::string& osrm_url) {
        py::gil_scoped_release release;
        auto result = get_best_route_osrm(start, end, osrm_url);
        py::gil_scoped_acquire acquire;
        return result;
    },
          py::arg("start"), py::arg("end"), py::arg("osrm_url") = "http://router.project-osrm.org",
          "Get best route using OSRM (returns BestRouteData)");

    m.def("get_alternative_routes_osrm", [](const Point& start, const Point& end, int num_alternatives, const std::string& osrm_url) {
        py::gil_scoped_release release;
        auto result = get_alternative_routes_osrm(start, end, num_alternatives, osrm_url);
        py::gil_scoped_acquire acquire;
        return result;
    },
          py::arg("start"), py::arg("end"), py::arg("num_alternatives") = 3, py::arg("osrm_url") = "http://router.project-osrm.org",
          "Get alternative routes using OSRM");

    m.def("get_closest_routes_osrm", [](const Point& point, int num_routes, const std::string& osrm_url) {
        py::gil_scoped_release release;
        auto result = get_closest_routes_osrm(point, num_routes, osrm_url);
        py::gil_scoped_acquire acquire;
        return result;
    },
          py::arg("point"), py::arg("num_routes") = 3, py::arg("osrm_url") = "http://router.project-osrm.org",
          "Get closest routes using OSRM");
}