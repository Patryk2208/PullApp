cmake_minimum_required(VERSION 3.25)
project(route_calc)

add_subdirectory(cpp)

set(PYBIND11_FINDPYTHON ON)
find_package(pybind11 REQUIRED)

pybind11_add_module(route_calc_algorithms_module
    route_calc/algorithms/bindings.cpp
)

target_link_libraries(route_calc_algorithms_module PRIVATE route_calc_algorithms)

set_target_properties(route_calc_algorithms_module PROPERTIES
    LIBRARY_OUTPUT_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/route_calc/algorithms"
)