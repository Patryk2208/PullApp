# Route-Calc Service

## Overview

The route-calc service is responsible for all geospatial routing calculations in the system. It consumes jobs from a RabbitMQ compute-queue, executes routing algorithms, and publishes results to a results-queue.

The service uses a hybrid architecture:
- **C++ core**: Implements routing algorithms for maximum performance
- **Python wrapper**: Handles I/O (RabbitMQ, PostGIS, Redis) using mature ecosystem libraries
- **pybind11**: Provides zero-overhead FFI between Python and C++

This approach allows complex algorithm development in C++ while keeping infrastructure code simple and maintainable in Python.

---

## Core Responsibilities

### 1. Standard Route Calculation
Compute optimal paths between two points. Supports multiple cost functions:
- Shortest distance
- Fastest time (using speed limits)
- Scenic routes (custom weighting)

### 2. Closest Routes Query
For a given geographic point, return the K nearest routes. Considers both:
- Straight-line proximity to route geometry
- Road-network access cost to reach the route

### 3. Detour Optimization (Uber-like)
Given a driver's existing route and a rider's desired start/end points, find the route that best covers the rider's trip while minimizing deviation from the driver's original path. The driver's route is prioritized rather than dictated.

### 4. Multi-Route Pareto Optimization
Given multiple riders and drivers, find sets of routes that collectively cover rider requests with minimal changes to driver routes. Explores trade-offs between:
- Coverage quality
- Total driver deviation
- Number of drivers involved

---

## Architectural Decisions

### Language Choice: Python + C++ with pybind11

**Rationale:**
- C++ delivers maximum performance for graph algorithms and spatial computations
- Python provides mature I/O libraries (RabbitMQ, PostGIS, Redis) with simple, proven APIs
- pybind11 enables seamless integration without network overhead or serialization
- Interactive Python REPL/notebook workflow accelerates algorithm experimentation for requirements #3 and #4

### FFI Strategy: pybind11

**Rationale:**
- Same technology powering numpy, tensorflow, and scikit-learn
- Automatic type conversion between C++ STL and Python types
- Zero-copy data sharing for large route geometries
- C++ exceptions map cleanly to Python exceptions
- Memory ownership semantics are explicit and manageable

### Queue Pattern: Asynchronous Job Processing

**Rationale:**
- Decouples request submission from computation
- KEDA-based autoscaling handles variable load
- Clear separation of concerns: trip-planner submits jobs, route-calc processes them

### Graph Storage: PostGIS + pgRouting

**Rationale:**
- Single source of truth for road network data
- Leverages existing PostGIS infrastructure
- pgRouting provides baseline routing for requirements #1 and #2
- Graph subsets can be loaded into C++ memory for custom algorithms

### Caching: Redis

**Rationale:**
- Redundant computation avoidance for frequent routes
- TTL-based expiration handles route changes
- Shared cache across route-calc instances

---

## Algorithms Summary

| Requirement | Algorithm | Implementation Layer | Notes |
|-------------|-----------|---------------------|-------|
| #1 Best route | A* with Euclidean heuristic | C++ core | Pluggable cost functions; can upgrade to Contraction Hierarchies if performance needed |
| #2 K closest routes | Spatial index + KNN | PostGIS + C++ | GiST indexes for proximity; access cost optionally computed in C++ |
| #3 Covering route | Detour A* with penalty | C++ core | Custom heuristic penalizes edges not on driver's route |
| #4 Optimal route set | Pareto frontier + greedy set cover | C++ core | Experimental; genetic algorithm optional for larger instances |

---

## Dependencies

### Core Dependencies
- **PostGIS**: Road network data and spatial queries
- **pgRouting**: Baseline routing functions
- **Redis**: Route result caching
- **RabbitMQ**: Job queue transport

### C++ Build Dependencies
- **pybind11**: Python binding generation
- **libpqxx**: PostgreSQL client (optional, if C++ queries PostGIS directly)
- **Boost.Graph** or **Valhalla**: Graph primitives (decision pending implementation phase)

### Python Dependencies
- **pika**: RabbitMQ client
- **psycopg2**: PostgreSQL client with PostGIS support
- **redis-py**: Redis client

---

## Deployment Model

The service runs as a containerized application scaled horizontally by KEDA based on RabbitMQ queue depth. Each instance contains both the Python wrapper and C++ core in a single container, with pybind11 linking them at build time.

**Scaling characteristics:**
- Stateless across instances (Redis provides shared cache)
- Graph data loaded from PostGIS on startup and cached in C++ memory
- KEDA monitors compute-queue depth and scales pods accordingly