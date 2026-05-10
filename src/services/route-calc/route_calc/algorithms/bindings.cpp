#include <pybind11/pybind11.h>
#include <pybind11/stl.h>
#include "mock_alg.hpp"

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
}