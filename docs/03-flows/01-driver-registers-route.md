Actors: Driver
Preconditions: Driver has verified account, logged in

Happy Path:
1. Driver interface → API Gateway → Trip-planner
   POST /api/driver/route
   {start, end}

2. Trip Planner validates:
   - Driver status is offline (Accounts via gRPC)
   - Destination is in service area (PostGIS query)
   - ...

3. Trip-Planner pushes event to ComputeQueue "find route" and stores id to cache

4. Route-Calc computes route with Geometry (polyline)

5. Route-Calc pushes results to ResultsQueue

6. Trip-Planner reads and metches from cache and stores in PostGIS:
   - driver_route record with LINESTRING

7. Trip-Planner → API Gateway → Driver app
   Response: { route_id, eta_to_destination }

Cross-Context Interactions:
- Step 2: Ride Operations → Identity (verify driver)
- Step 3: Ride Operations internal (Trip Planner -> Compute Queue -> Route Calc)
- Step 4: Ride Operations internal (Route Calc -> Results Queue -> Trip Planner)

Failure Scenarios:
| Scenario | Handling |
|----------|----------|
| Driver not verified | Reject with "complete verification first" |
| Destination outside Poland | Reject with "service area limited" |
| Route Calc timeout | Retry once, then reject with "try again" |

Latency Requirement: < 2 seconds
Data Consistency: Strong (PostgreSQL transaction)