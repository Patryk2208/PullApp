# Chat Service — Bounded Context

## Overview

Scoped real-time messaging between a driver and passenger for the duration of a single ride. Rooms are created by Trip Planner via gRPC — Chat has no ride logic of its own. Two participants per room, always. Text-only, no attachments.

---

## Actors

| Actor | Interaction |
|-------|-------------|
| Trip Planner | gRPC CreateRoom — initiates the chat room when a ride is matched |
| Driver app | WebSocket `/chat/{rideId}` — sends and receives messages |
| Passenger app | WebSocket `/chat/{rideId}` — sends and receives messages |

---

## Flow 1: Room creation

Trip Planner calls Chat Service gRPC when a ride is confirmed:

```protobuf
rpc CreateRoom(CreateRoomRequest) returns (CreateRoomResponse);

message CreateRoomRequest {
    string ride_id    = 1;
    string driver_id  = 2;
    string passenger_id = 3;
}
```

Chat stores room metadata in Redis:

```
SET chat:room:{rideId} {"driverId": "...", "passengerId": "..."}
```

No expiry set here — Trip Planner or a ride-end event is responsible for cleanup (see below).

---

## Flow 2: Client connects

```
Client → WS /chat/{rideId}
```

On connect:
1. Validate JWT (API Gateway, already done before request reaches Chat)
2. Read `chat:room:{rideId}` from Redis — if missing, reject with 404
3. Confirm requesting userId is either driverId or passengerId in the room — if not, reject with 403
4. Replay message history: query MongoDB for messages where `rideId = X` ordered by `sentAt`, push to socket
5. Register connection in local hub: `hub[rideId][userId] = conn`
6. Enter read loop

---

## Flow 3: Message send

Client sends over WebSocket:

```json
{ "text": "I'm at the blue gate" }
```

Chat Service:
1. Persist to MongoDB: `{ messageId, rideId, senderId, text, sentAt }`
2. Look up other participant's connection in local hub
3. If found (same pod): push directly to their socket
4. If not found (different pod): **TODO — hash-based routing at ingress level, see scaling note**

---

## Flow 4: Ride end / room teardown

Trip Planner calls:

```protobuf
rpc CloseRoom(CloseRoomRequest) returns (CloseRoomResponse);

message CloseRoomRequest {
    string ride_id = 1;
}
```

Chat Service:
1. Deletes `chat:room:{rideId}` from Redis
2. Closes both WebSocket connections gracefully (send close frame)
3. MongoDB messages are retained for 30 days via TTL index, then deleted automatically

---

## Scaling

**Target:** hash-based routing at ingress — both driver and passenger for a given `rideId` are always routed to the same pod. With this guarantee, the cross-pod problem does not exist: both connections live in the same in-memory hub.

**Implementation:** `hash(rideId) % podCount` at the API Gateway (YARP custom load balancing policy) or nginx consistent hashing upstream. **This is a TODO** — the service is correct without it as long as a single Chat pod is running. Multi-pod without this routing will silently fail to deliver messages to the other participant.

**Until routing is implemented:** run Chat as a single pod (replicas: 1 in K8s). Acceptable for development and early testing.

---

## Redis schema

| Key | Type | Value | TTL |
|-----|------|-------|-----|
| `chat:room:{rideId}` | String (JSON) | `{driverId, passengerId}` | None — deleted by CloseRoom gRPC |

---

## MongoDB schema

```js
// Collection: messages
// TTL index: db.messages.createIndex({ sentAt: 1 }, { expireAfterSeconds: 2592000 }) // 30 days
{
    _id:       ObjectId,
    rideId:    String,   // indexed
    senderId:  String,
    text:      String,
    sentAt:    Date      // indexed (compound index with rideId for history queries)
}
```

---

## What this service does NOT do

- Does not validate whether a ride is active beyond checking room existence in Redis
- Does not know about ride state transitions (Trip Planner owns this)
- Does not support group messaging, attachments, or read receipts
- Does not publish to Kafka
- Does not write to PostGIS
