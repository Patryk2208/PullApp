# PullApp - Sprint 4 Status

## Team & Timeline
- Team of 4, semester project
- Currently in Sprint 4
- Sprint 4 goals: frontend, algorithms, trip-planner core business logic

## What's Built

### Trip Planner (.NET)
- Infra layer wired: RabbitMQ, Redis, DB integration
- Some domain scaffolding, but no meaningful business logic yet
- Treat as near-greenfield for core logic purposes

### Route-Calc (C++20 + OSRM)
- Consumer architecture complete: event loop → thread pool → algorithm call → ack/nack
- Algorithm is a sleeping mock (pybind C++ call placeholder)
- Structurally complete, algorithms are the remaining work

### Accounts (.NET)
- Pretty much done

### API Gateway (YARP)
- Done

### K8s
- Complete config with overlay/local environment
- ExternalNames from Docker Compose
- KEDA mechanism live and tested this morning — Route-Calc pods react to queue depth stress tests

### Driver Tracker
- Not started / out of scope for now

### Payments
- Not started / out of scope for now

### Frontend (React Native)
- In progress this sprint

## Core Flows to Design & Implement (Trip Planner focus)

### Flow 1: Driver Publishes Route
- Specced in `01-driver-registers-route.md`
- Reasonably well documented, implementation pending

### Flow 2: Passenger Requests Match
- Passenger submits request → Trip Planner → ComputeQueue → Route-Calc → ResultsQueue → Trip Planner → SSE to Passenger (success or failure)
- Core sequence agreed, details TBD

## Open Design Decisions

### Matching approach (see `02-passenger-requests-match.md`)
Two unresolved axes:

**Axis A - Who chooses the route?**
- Option 1: Ranked routes returned to Passenger, Passenger selects one
- Option 2: Routes sent async to Drivers, first to accept wins

**Axis B - Is the Driver's route modified?**
- Option 1: Route NOT modified, Driver+Passenger coordinate via Chat
- Option 2: Route IS modified, Driver must accept the modification

Files `02-1.md` through `02-4.md` cover the combinations — none fully specced yet.

## Immediate Focus
Designing, documenting, and implementing Trip Planner core business logic:
- State machine for rides/requests
- Happy path sequences for both flows
- API contracts (endpoints, payloads, SSE event shapes)
- Error/edge cases