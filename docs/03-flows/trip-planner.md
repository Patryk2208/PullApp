# Trip-Planner Service Flows

This document summarizes the core business flows of the Trip-Planner service as implemented in the API endpoints.

## Flow 0: Route Submission (Driver)
*   **Endpoint:** `POST /driver/routes`
*   **Description:** Driver submits a new route.
*   **Process:**
    1.  Validates driver authorization (IAccountsService).
    2.  Creates route in `Created` status.
    3.  Queues geometry computation to `route-calc` via RabbitMQ.
*   **Result:** `202 Accepted`. Completion notification delivered via `RouteReadyEvent` (SSE/push).

## Flow 1: Route Activation (Driver)
*   **Endpoint:** `POST /driver/routes/{routeId}/activate`
*   **Description:** Driver signals they are at the start location and ready.
*   **Process:**
    1.  Verifies driver location is near `Route.Start`.
    2.  Transitions route to `Active`.
    3.  Transitions `WaitingForActivation` rides to `WaitingForDriver`.
*   **Result:** `204 No Content`.

## Flow 1.5: Route Deletion (Driver)
*   **Endpoint:** `DELETE /driver/routes/{routeId}`
*   **Description:** Driver deletes a route.
*   **Process:**
    1.  Notifies passengers with accepted rides via `RouteDeletedEvent`.
*   **Constraint:** Cannot delete `Active` routes with existing rides.
*   **Result:** `204 No Content`.

## Flow 2: Route Search (Passenger)
*   **Endpoint:** `POST /passenger/routes/search`
*   **Description:** Passenger searches for available routes.
*   **Process:**
    1.  Queues search/matching to `route-calc` via RabbitMQ.
*   **Result:** `202 Accepted`. Results delivered via `RouteSearchCompletedEvent` (SSE/push).

## Flow 3: Ride Request (Passenger)
*   **Endpoint:** `POST /passenger/routes/{routeId}/requests`
*   **Description:** Passenger requests a seat on a specific route.
*   **Process:**
    1.  Quotes price and freezes funds via Payments service.
    2.  Notifies driver via `RideRequestedEvent`.
*   **Result:** `201 Created`.

## Flow 4: Ride Request Rejection (Driver)
*   **Endpoint:** `POST /driver/requests/{requestId}/reject`
*   **Description:** Driver rejects a pending request.
*   **Process:**
    1.  Unfreezes passenger funds via Payments service.
    2.  Notifies passenger via `RideRejectedEvent`.
*   **Result:** `204 No Content`.

## Flow 5: Ride Request Acceptance (Driver)
*   **Endpoint:** `POST /driver/requests/{requestId}/accept`
*   **Description:** Driver accepts a pending request.
*   **Process:**
    1.  Atomically creates a `Ride`.
    2.  If route becomes full, marks it `Full` and auto-rejects other pending requests.
    3.  Opens a chat room (IChatService).
    4.  Notifies passenger via `RideAcceptedEvent`.
*   **Result:** `200 OK` with `rideId` and `chatRoomId`.

## Flow 7: Pickup Declaration (Driver & Passenger)
*   **Endpoints:**
    *   `POST /driver/rides/{rideId}/pickup`
    *   `POST /passenger/rides/{rideId}/pickup`
*   **Description:** Both parties confirm the pickup has occurred.
*   **Sequence:** Driver MUST declare first.
*   **Transition:** Ride transitions to `Started` only after both declarations are recorded.
*   **Result:** `204 No Content`.

## Flow 8 (a or b): Ride Cancellation (Passenger)
*   **Endpoint:** `DELETE /passenger/rides/{rideId}`
*   **Description:** Passenger cancels a ride before it starts.
*   **Scenarios:**
    1.  **WaitingForActivation:** Full refund, funds unfrozen.
    2.  **WaitingForDriver:** Cancellation fee charged, remainder unfrozen.
*   **Constraint:** Cannot cancel `Started` rides via this endpoint.
*   **Result:** `204 No Content`.

## Flow 8c: Ride Completion (Driver & Passenger)
*   **Endpoints:**
    *   `POST /passenger/rides/{rideId}/end`
    *   `POST /driver/rides/{rideId}/end`
*   **Description:** Both parties declare the ride is finished.
*   **Sequence:** Passenger MUST declare first.
*   **Finalization:** When both have declared:
    1.  Payment is charged via Payments service.
    2.  Publishes `RideCompletedEvent` and `RideEndedEvent`.
*   **Result:** `204 No Content`.
