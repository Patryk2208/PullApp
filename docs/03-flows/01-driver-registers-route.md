Actors: Driver
Preconditions: Driver has verified account, logged in

This architecture decouples a compute-heavy route calculation service from a synchronous API gateway using a message queue with event-driven completion. The driver app polls for results while the Trip Planner is notified of completion via RabbitMQ, eliminating any polling loops inside the server.
Process Description:

1. Driver submits route request → API Gateway → Trip Planner
2. Trip Planner validates driver status (via gRPC) and service area (via PostGIS)
3. Job is persisted in PostGIS with status pending, storing job ID and correlation ID
4. Message is published to route-calc-jobs queue with reply_to header pointing to Trip Planner's reply queue and a unique correlation_id
5. Immediate response returns 202 Accepted with job_id to the driver
6. Route-Calc workers, auto-scaled by KEDA based on queue depth, pull jobs and compute optimal routes
7. Upon completion, Route-Calc publishes the result to trip-planner-replies queue, preserving the correlation_id
8. Trip Planner's background consumer receives the reply and updates the corresponding job record in PostGIS to completed, storing the route geometry and ETA
9. Driver polls the status endpoint (GET /api/route/{job_id}), which performs a simple database read and returns the route once completed

No component polls the database for status changes. The Trip Planner is entirely event-driven, relying on RabbitMQ to deliver completion notifications.

---

## Architecture Diagram

```mermaid
sequenceDiagram
    participant Driver as Driver App
    participant API as API Gateway
    participant Planner as Trip Planner
    participant DB as PostGIS
    participant Queue as ComputeQueue
    participant Reply as ResultsQueue
    participant Calc as Route-Calc Worker<br/>(KEDA-scaled)
    participant Accounts as Accounts Service

    Driver->>API: POST /api/route {start, end}
    API->>Planner: Forward request

    Planner->>Accounts: gRPC: validate driver status
    Accounts-->>Planner: driver online
    Planner->>DB: PostGIS: validate service area
    DB-->>Planner: within area

    Planner->>DB: INSERT job (pending, job_id, correlation_id)
    Planner->>Queue: publish {job_id, correlation_id, start, end}<br/>reply_to: trip-planner-replies
    Planner-->>API: 202 Accepted {job_id}
    API-->>Driver: {job_id}

    Note over Queue,Calc: KEDA scales Route-Calc workers based on queue depth

    Queue-->>Calc: deliver job (push)
    Calc->>Calc: compute optimal route (0-10s)
    Calc->>Reply: publish result {route, eta}<br/>correlation_id
    Reply-->>Planner: deliver result

    Planner->>DB: UPDATE job SET status=completed, route, eta<br/>WHERE correlation_id = ...

    loop Driver Polling
        Driver->>API: GET /api/route/{job_id}
        API->>Planner: forward
        Planner->>DB: SELECT status, route, eta
        DB-->>Planner: completed + route data
        Planner-->>API: 200 OK {route, eta}
        API-->>Driver: route data
    end