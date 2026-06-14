# Driver Tracker — Bounded Context

## Overview

Responsible for two things only:
1. Accepting GPS position updates from drivers (HTTP POST, stateless)
2. Streaming live driver position to the matched passenger (WebSocket, goroutine per connection)

No ride state logic. No matching logic. Reads Redis, writes Redis. Trip Planner owns ride lifecycle and is responsible for cache cleanup on ride end/cancel.

---

## Actors

| Actor | Interaction |
|-------|-------------|
| Driver app | HTTP POST /position — fire and forget |
| Passenger app | WebSocket /track/{routeId} — receives position ticks |
| Trip Planner | Deletes `position:{routeId}` from Redis on ride completion or cancellation |
| Route-Calc | Reads `position:{routeId}` from Redis during matching (read-only, no coordination needed) |

---

## Flow 1: Driver posts position

```
Driver App → POST /position
            { routeId, lat, lng, timestamp }

Driver Tracker → Redis SET position:{routeId} { lat, lng, timestamp } EX 30
```

- No authentication of routeId here beyond JWT validation at the gateway — Trip Planner owns route validity
- TTL of 30s acts as implicit heartbeat: stale drivers fall out of cache automatically without explicit cleanup
- Returns 204, driver app fires next POST on its own interval (suggested: every 3–5s)

---

## Flow 2: Passenger tracks driver

```
Passenger App → WS /track/{routeId}
```

On connect, Driver Tracker spawns one goroutine per connection:

```go
func streamPosition(ctx context.Context, conn *websocket.Conn, routeID string) {
    ticker := time.NewTicker(5 * time.Second)
    defer ticker.Stop()
    for {
        select {
        case <-ctx.Done():
            return
        case <-ticker.C:
            pos, err := redis.Get(ctx, "position:"+routeID).Result()
            if err == redis.Nil {
                // route gone (ride ended, Trip Planner cleaned up)
                conn.WriteJSON(map[string]string{"event": "ride_ended"})
                return
            }
            conn.WriteJSON(pos)
        }
    }
}
```

A concurrent read pump cancels the context immediately on passenger disconnect — goroutine exits, no leak:

```go
func readPump(cancel context.CancelFunc, conn *websocket.Conn) {
    defer cancel()
    for {
        if _, _, err := conn.ReadMessage(); err != nil {
            return
        }
    }
}
```

Passenger only receives, never sends. `ReadMessage` blocking on a receive-only socket exits on close/error, which is the disconnect signal.

---

## Redis schema

| Key | Type | Value | TTL |
|-----|------|-------|-----|
| `position:{routeId}` | String (JSON) | `{lat, lng, timestamp}` | 30s (refreshed on each POST) |

TTL serves as the liveness heartbeat. No separate cleanup job needed for crashed/offline drivers.

---

## Cleanup responsibility

| Event | Responsible | Action |
|-------|-------------|--------|
| Driver stops posting (crash, offline) | TTL | Key expires after 30s automatically |
| Ride completed normally | Trip Planner | `DEL position:{routeId}` |
| Ride cancelled | Trip Planner | `DEL position:{routeId}` |
| Passenger disconnects | Driver Tracker | Context cancelled, goroutine exits, nothing to clean in Redis |

---

## Scaling

Truly stateless on the POST side — any pod handles any driver, no affinity needed.

WebSocket side holds connections, so horizontal scaling needs sticky sessions (already configured in K8s ingress for other WebSocket services). Each pod independently reads from Redis — no cross-pod coordination, no pub/sub.

---

## What this service does NOT do

- Does not validate whether routeId belongs to the posting driver (Gateway JWT + Trip Planner own that)
- Does not know about ride state (active, cancelled, completed)
- Does not write to PostGIS
- Does not publish to Kafka
- Does not hold any driver metadata
