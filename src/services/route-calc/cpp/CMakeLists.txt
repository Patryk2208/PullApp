find_package(CURL QUIET)
if(NOT CURL_FOUND)
    message(WARNING "CURL not found - OSRM client will be disabled. Install libcurl to enable OSRM support.")
    add_definitions(-DOSRM_DISABLED)
else()
    message(STATUS "CURL found - OSRM client enabled")
endif()

find_package(nlohmann_json 3.2.0 QUIET)
if(NOT nlohmann_json_FOUND)
    message(STATUS "nlohmann_json not found, will use header-only include")
endif()

add_library(route_calc_algorithms
    src/mock_alg.cpp
    src/osrm_client.cpp
)

target_include_directories(route_calc_algorithms PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include)
if(CURL_FOUND)
    target_link_libraries(route_calc_algorithms PRIVATE CURL::libcurl)
endif()