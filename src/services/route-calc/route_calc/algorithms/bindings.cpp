#include <pybind11/pybind11.h>
#include "mock_alg.hpp"

namespace py = pybind11;

PYBIND11_MODULE(route_calc_algorithms_module, m) {
    m.doc() = "Mock routing algorithm for testing";
    m.def("slow_algorithm", [](double input, int seconds) {
        py::gil_scoped_release release;
        slow_algorithm(input, seconds);
        py::gil_scoped_acquire acquire;
    },
          py::arg("input"), py::arg("seconds"),
          "Sleeps for N seconds (releases GIL)");
}