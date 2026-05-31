# Route-Calc / Trip-Planner Refactor Plan

## Context

route-calc is a Python + C++ (pybind11) stateless compute service. It consumes jobs from RabbitMQ (`compute-queue`), runs algorithms, and publishes results to `results-queue`. The C++ layer exposes `match_single_route`, `distance_km`, OSRM wrappers via pybind11. trip-planner is the .NET 10 producer/consumer on the other side.

The branch `feature/route-calc/route-matching-cpp` (Wiktor's work) was rebased onto develop with schema conflicts and an incomplete pb2 regeneration. This plan tracks the full reconciliation.

---

## Architecture Decisions

- **Shared PostGIS DB**: trip-planner owns the DB (writes), route-calc reads from it (read-replica friendly for scaling). route-calc queries `routes` table directly for candidates.
- **Two active algorithms**: `BEST_ROUTE` (OSRM, flow 0 — driver creates route) and `RIDE_MATCHING` (C++ algorithm, flow 2 — passenger searches). `CLOSEST_ROUTES` is defined in proto but has no caller yet.
- **No candidate_routes in message**: trip-planner does NOT bundle driver routes in the `RideMatchingQuery` message. route-calc fetches `Active` routes from PostGIS itself, then runs C++ matching.
- **trip-planner writes geometry**: `BestRouteResult.points[]` is serialized to a GeoJSON string by `RouteComputedHandler` and stored in `routes.geometry_json` until the PostGIS migration (step 4).

---

## Proto Schema (DONE — step 1)

File: `src/schemas/pullapp/core/v1/queue.proto`

### ComputeMessage (trip-planner → route-calc)
- `job_id` (1), `algorithm` (2), `requesting_user_id` (3), `created_at` millis (4), `retry_count` (5)
- `oneof params`: `BestRouteParams` (20), `RideMatchingQuery` (22)

### BestRouteParams
- `start`, `end`, `cost_type`

### RideMatchingQuery
- `passenger_id`, `start`, `end`, `departure_date`, `seats_needed`, `max_detour_km` (6), `time_window_minutes` (7)
- No `candidate_routes` — route-calc fetches them from DB

### ResultMessage (route-calc → trip-planner)
- `job_id` (1), `success` (2), `error` (3)
- `oneof result`: `BestRouteResult` (20), `RideMatchingResult` (22)

### BestRouteResult
- `points[]`, `distance_meters`, `duration_seconds`

### RideMatchingResult
- `matches[]` of `MatchedRoute{route_id, driver_id, match_score, detour_km, pickup_point_index, dropoff_point_index}`

### Defined but not wired (CLOSEST_ROUTES)
- `ClosestRoutesParams`, `ClosestRoute`, `ClosestRoutesResult` — present in proto, not in any oneof

---

## Step 1 — Proto schema ✅

Updated `src/schemas/pullapp/core/v1/queue.proto` as above.

---

## Step 2 — Model / DTO layer ✅

### Route-calc
- Local schema copy (`src/services/route-calc/schemas/...`) synced to canonical
- `queue_pb2.py` regenerated via protoc
- `model/algorithms.py`: `RideMatchingQuery` stripped of `candidate_routes`; `DriverRoute` kept as internal model for DB fetch (step 3)
- `model/messages.py`: `ComputeMessage.from_proto` handles `best_route` / `ride_matching` only; `created_at` parsed as millis; `requesting_user_id` preserved
- `algorithms/algorithms_orchestrator.py`: `_match_rides` stubbed with `candidate_routes = []` and `# TODO: fetch from PostGIS (step 3)`
- `tests/integration_test.py`: config path fixed (`./route_calc/config.json`)

### Trip-planner
- `Queue.cs` auto-regenerated at build time via `Grpc.Tools` from the canonical proto
- `Domain/Compute/Messages.cs`: `DriverRouteComputeJob` → `BestRouteComputeJob`, `PassengerMatchComputeJob` → `RideMatchingComputeJob`, same for results; `JobType` enum: `BestRoute` / `RideMatching`
- `Domain/Compute/JobTypes.cs`: `BestRouteJobPayload(Start, End, CostType)`, `RideMatchingJobPayload(Start, End, DepartureDate, SeatsNeeded, MaxDetourKm, TimeWindowMinutes)`, `BestRouteJobResult(Points[], DistanceMeters, DurationSeconds)`, `RideMatchingJobResult(Matches[])`, `MatchEntry(RouteId, DriverId, MatchScore, DetourKm, PickupPointIndex, DropoffPointIndex)`
- `Infrastructure/Queue/Mappers.cs`: both mappers rewritten against new proto
- `Application/Features/Driver/CreateRoute.cs`: publishes `BestRouteComputeJob`
- `Application/Features/Passenger/SubmitRouteSearch.cs`: publishes `RideMatchingComputeJob`; command extended with `DepartureDate`, `SeatsNeeded`, `MaxDetourKm`, `TimeWindowMinutes`
- `Api/Endpoints/Passenger/SubmitRouteSearchEndpoint.cs`: request extended with new fields
- `Application/Features/Background/RouteComputedHandler.cs`: handles `BestRouteComputeResult` / `RideMatchingComputeResult`; serializes `Points[]` to GeoJSON string (temporary, see step 4)
- `Domain/Events/DomainEvents.cs`: `RouteReadyEvent` updated to `(DistanceMeters: double, DurationSeconds: double)`
- All test files updated to compile (types renamed, assertions adjusted for new shapes)

**Known blocker**: committed `.so` is `cpython-313`, venv runs Python 3.14. C++ module needs rebuild (`poetry install` with Python 3.13 or rebuild `.so` for 3.14).

---

## Step 3 — route-calc PostGIS read (TODO)

### `route_calc/infra/db.py`
Open a psycopg connection pool using `config["trip_planner_db"]`. Expose a method:

```python
async def get_active_routes(self) -> List[DriverRoute]:
    # SELECT id, driver_id, capacity - active_ride_count AS seats_free,
    #        ST_AsText(route_geom) AS geom_wkt
    # FROM routes
    # WHERE status = 'Active' AND capacity - active_ride_count > 0
```

Parse WKT `LINESTRING(lon lat, ...)` into `List[Point]` for `DriverRoute.route_points`. Map to `DriverRoute` (internal model in `model/algorithms.py`).

Note: `route_geom` column does not exist yet (it's still `geometry_json` string) — this step is partially blocked by step 4. Can prototype with the string column and parse GeoJSON instead, then switch to PostGIS geometry once step 4 lands. Or implement step 4 first.

### `AlgorithmsOrchestrator`
- Takes `db: DB` dependency injected at construction
- `_match_rides` calls `self.db.get_active_routes()` replacing the `candidate_routes = []` stub

### `main.py`
- Instantiate `DB(config["trip_planner_db"])`, pass to `AlgorithmsOrchestrator`

---

## Step 4 — PostGIS geometry column in trip-planner (TODO)

### DB migration
- Drop `geometry_json TEXT`, `eta_seconds INT`, `distance_meters INT` columns
- Add `route_geom geometry(LineString, 4326)` with GIST index
- `eta_seconds` → `duration_seconds FLOAT` (rename + type change)
- `distance_meters` → keep as `distance_meters FLOAT`

### trip-planner domain + repository
- `Route.SetGeometry(string, int, int)` → `SetGeometry(IReadOnlyList<GeoPoint> points, double distanceMeters, double durationSeconds)`
- `Route.RouteGeom: IReadOnlyList<GeoPoint>?` replaces `GeometryJson: string?`
- Enable `NetTopologySuite` on Npgsql data source (one line in Program.cs)
- `PostgresRouteRepository`: write `route_geom` as PostGIS `LineString` via NTS; read back
- `RouteComputedHandler`: remove `PointsToGeoJson` helper; call `route.SetGeometry(r.Result.Points, ...)` directly
- `RouteReadyEvent`: replace `GeometryJson` with `IReadOnlyList<GeoPoint> Points`

### route-calc db.py
- Switch from parsing `geometry_json` string to reading `ST_AsText(route_geom)` WKT

---

## Step 5 — Fix C++ `.so` / rebuild (TODO)

The committed `.so` (`cpython-313`) does not load under Python 3.14. Options:
- Pin the venv to Python 3.13 (update `pyproject.toml` — already says `>=3.13,<3.15`, so 3.14 is in range but the built artifact doesn't match)
- Rebuild: `poetry install` triggers `scikit-build-core` + CMake build, produces correct `.so` for active Python
- Remove committed `.so` from repo; add to `.gitignore`; build in CI/Dockerfile only

Dockerfile already handles the build correctly (it builds from source inside the container).

---

## Step 6 — Tests (TODO)

After steps 3–5:
- `tests/algorithm_test.py`: already passes against C++ module (once `.so` rebuilt)
- `tests/consumer_test.py`: passes once imports resolve
- `tests/rabbitmq_e2e_test.py`: requires route-calc service + RabbitMQ running (docker-compose e2e profile)
- `tests/rabbitmq_integration_test.py`: requires RabbitMQ running
- Trip-planner unit tests: build and pass now; integration tests need Testcontainers
- `ComputeMapperTests`: should pass with new proto (pure serialization, no containers)

---

## Known Sketchy Things (non-blocking)

- `algorithms_orchestrator.py`: env-var `ROUTE_CALC_MOCK_FAILURE_PROBABILITY=0.1` default causes 10% random failures — tests should set it to `0`
- `consumer.py`: lives in `api/` package; should be moved to top-level or `infra/`
- `threading.Lock` in `JobContext` is named `useless_mutex` — rename to `state_lock` or similar; consumer has some minor lint items
- `integration_test.py`: reads config and publishes to live queue — not a proper unit test, more of a manual smoke test
- `ClosestRoutesResult` is in proto but `ResultMessage.to_proto()` in Python doesn't handle it — will raise silently if somehow triggered