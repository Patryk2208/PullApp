find_package(nlohmann_json 3.2.0 QUIET)
if(NOT nlohmann_json_FOUND)
    message(STATUS "nlohmann_json not found, will use header-only include")
endif()

find_package(CURL REQUIRED)

add_library(route_calc_algorithms
    src/mock_alg.cpp
    src/osrm_client.cpp
)

set_target_properties(route_calc_algorithms PROPERTIES
    POSITION_INDEPENDENT_CODE ON
)

target_include_directories(route_calc_algorithms PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include)
target_link_libraries(route_calc_algorithms PRIVATE CURL::libcurl)

option(ROUTE_CALC_BUILD_GEOMETRY_TEST "Build offline geometry parse test" ON)
if(ROUTE_CALC_BUILD_GEOMETRY_TEST)
    add_executable(geometry_parse_test tests/geometry_parse_test.cpp)
    target_link_libraries(geometry_parse_test PRIVATE route_calc_algorithms)
    target_include_directories(geometry_parse_test PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/include)
endif()
