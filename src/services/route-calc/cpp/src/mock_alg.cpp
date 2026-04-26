#include <thread>
#include <chrono>
#include <vector>

std::vector<double> slow_algorithm(double input, int seconds) {
    std::this_thread::sleep_for(std::chrono::seconds(seconds));
    return {input, input * 2};
}