## Opis

Notifications is a Go service responsible for two things: consuming domain events from Kafka (published by trip-planner) and pushing them to connected clients over SSE. It owns the SSE layer for the entire platform — trip-planner no longer holds any SSE connections.

Each authenticated client opens one long-lived SSE stream. The service fans out the right events to the right users based on the `DriverId` / `PassengerId` fields in each event payload.

---

## Architektura

```
Kafka (3 topics)
    └─ KafkaConsumer (goroutine per topic)
           └─ Dispatcher
                  └─ SseHub  ←→  client SSE connections (one per user)
```

Three layers:

- **`internal/model/event.go`** — already written. Contains `Envelope`, all payload structs, topic + event type constants, and `DecodePayload[T]`.
- **`internal/service/`** — business logic: `Dispatcher` (routes envelopes to users), `SseHub` (manages connections).
- **`internal/transport/`** — HTTP handler for the SSE endpoint + Kafka consumer wiring.

---

## SSE Hub

`SseHub` tracks one channel per connected user ID. API:

```go
type SseHub interface {
    // Register opens a channel for userId. Returns the channel and a cancel func.
    Register(userId string) (<-chan string, func())

    // Send delivers a JSON-encoded SSE data line to userId if connected.
    // No-op if user has no open connection.
    Send(userId string, event string, data string)
}
```

Implementation notes:
- Store `map[string]chan string` protected by a mutex (or use a sync-safe structure).
- `Register` creates a buffered channel (capacity ~32) and stores it. The cancel func removes the entry and closes the channel.
- `Send` looks up the channel and does a non-blocking send (drop if full — client reconnects and catches up).

---

## Dispatcher

`Dispatcher` receives a decoded `model.Envelope`, switches on `EventType`, decodes the payload, and calls `hub.Send` for the correct user(s).

Routing table:

| EventType | Who receives the SSE event |
|-----------|---------------------------|
| `route_selected` | `DriverId` — passenger selected their route, driver must confirm |
| `match_confirmed` | `PassengerId` |
| `match_declined` | `PassengerId` |
| `driver_arrived` | `PassengerId` |
| `ride_started` | `PassengerId` and `DriverId` |
| `ride_completed` | `PassengerId` and `DriverId` |
| `ride_cancelled` | the OTHER party: if `CancelledBy == "passenger"` → `DriverId`; if `"driver"` → `PassengerId`; if `"system"` → both |
| `ride_interrupted` | `PassengerId` |
| `rating_prompt` | `PassengerId` and `DriverId` |

The SSE event sent to the client mirrors the original envelope:

```
event: <EventType>
data: {"eventId":"...","occurredAt":"...","payload":{...}}
```

Pass the raw `Payload` bytes through — no re-encoding needed.

---

## Kafka Consumer

One consumer group per topic. Run each as a goroutine in `main`.

Topics to subscribe:
- `model.TopicRideCompletions` — `ride_completed`, `ride_cancelled`, `ride_interrupted`
- `model.TopicUserActions` — `route_selected`, `match_confirmed`, `match_declined`, `driver_arrived`, `ride_started`
- `model.TopicNotificationTriggers` — `rating_prompt`

For each message:
1. `json.Unmarshal` into `model.Envelope`
2. Pass envelope to `Dispatcher.Dispatch(ctx, envelope)`
3. Commit offset (manual commit after successful dispatch)

Consumer config: `auto.offset.reset = latest` (notifications are real-time; no catch-up needed). `enable.auto.commit = false`.

---

## SSE Endpoint

```
GET /stream
Authorization: Bearer <jwt>
Accept: text/event-stream
```

1. Extract user ID from the JWT (sub claim).
2. Call `hub.Register(userId)` to get the channel and cancel func.
3. Set headers: `Content-Type: text/event-stream`, `Cache-Control: no-cache`, `X-Accel-Buffering: no`.
4. Write `retry: 3000\n\n` once to tell clients to reconnect after 3 s.
5. Loop: read from channel, write `event: <name>\ndata: <json>\n\n`, flush.
6. On client disconnect (`r.Context().Done()`): call cancel func and return.

No auth on the event data itself — the stream is per-user by construction.

---

## Comms

**Inbound:** Kafka consumer (Confluent Go client — `github.com/confluentinc/confluent-kafka-go/v2`).

**Outbound:** SSE over HTTP to mobile/web clients (via API Gateway WebSocket upgrade or direct long-poll).

**No DB.** Notifications are fire-and-forget. Missed events (client offline) are not replayed — the client re-fetches state from trip-planner on reconnect.

---

## Configuration (env vars)

| Var | Example | Purpose |
|-----|---------|---------|
| `KAFKA_BOOTSTRAP_SERVERS` | `kafka:9092` | Broker address |
| `KAFKA_GROUP_ID` | `notifications` | Consumer group |
| `JWT_SECRET` | — | For validating Bearer tokens on `/stream` |
| `HTTP_PORT` | `8080` | Listening port |

---

## Implementation checklist for Claude Code

1. `internal/model/event.go` — **already done**.
2. `internal/service/hub.go` — implement `SseHub` with mutex-protected map + buffered channels.
3. `internal/service/dispatcher.go` — implement `Dispatcher.Dispatch` using the routing table above; use `model.DecodePayload[T]` for each branch.
4. `internal/transport/sse.go` — HTTP handler for `GET /stream`; JWT extraction, hub register, flush loop.
5. `internal/transport/consumer.go` — Kafka consumer factory; one goroutine per topic; deserialize envelope → dispatcher.
6. `cmd/notifications/main.go` — wire everything: read config from env, start consumers, start HTTP server.
7. `Dockerfile` — standard Go multi-stage build.