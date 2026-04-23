#include <pybind11/pybind11.h>
#include <thread>
#include <chrono>

namespace py = pybind11;

std::vector<double> slow_algorithm(double input, int seconds) {
    py::gil_scoped_release release;

    std::this_thread::sleep_for(std::chrono::seconds(seconds));

    py::gil_scoped_acquire acquire;

    return {input, input * 2};
}

PYBIND11_MODULE(mock_router, m) {
    m.doc() = "Mock routing algorithm for testing";
    m.def("slow_algorithm", &slow_algorithm,
          py::arg("input"), py::arg("seconds"),
          "Sleeps for N seconds (releases GIL)");

    m.def("cancellable_algorithm", &cancellable_algorithm,
          py::arg("input"), py::arg("seconds"), py::arg("should_cancel") = nullptr,
          "Sleeps with cancellation checkpoints");
}