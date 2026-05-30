# Testing the notifications service

## Test layout

| File | Kind | Needs Docker? |
|------|------|---------------|
| `internal/model/mapper_test.go` | unit + concurrency + load | no |
| `internal/model/bench_test.go` | benchmarks | no |
| `internal/service/dispatcher_test.go` | unit (mocked idempotency/sse/pusher) | no |
| `internal/service/streamer_test.go` | unit | no |
| `internal/service/notifier_test.go` | unit (push content mapping) | no |
| `internal/handler/stream_test.go` | unit (SSE handler via httptest) | no |
| `internal/postgres/repository_integration_test.go` | integration (`//go:build integration`) | yes |
| `internal/kafka/consumer_integration_test.go` | integration (`//go:build integration`) | yes |

The integration tests are gated behind the `integration` build tag, so the
default `go test ./...` (what CI runs) only executes the fast, hermetic unit
tests. They spin up real Postgres / Kafka via
[testcontainers-go](https://golang.testcontainers.org/) — the same approach
driver-tracker already uses.

## Unit tests (no Docker)

```bash
cd src/services/notifications

# everything fast
go test ./...

# with the race detector — important for the hub/mapper concurrency tests
go test -race ./...

# just the SSE hub, verbose
go test -race -v ./internal/model/
```

The race detector is the point of `TestConcurrentRegisterSendUnregister` and
`TestLoadFanout` — run those with `-race` or they prove little:

```bash
go test -race -run 'TestConcurrent|TestLoadFanout' ./internal/model/
```

`TestLoadFanout` skips under `-short`:

```bash
go test -short ./...        # skips the load test
```

## Benchmarks (no Docker)

```bash
go test -bench=. -benchmem ./internal/model/
go test -bench=BenchmarkSendParallel -cpu=1,4,8 ./internal/model/
```

## Integration tests (Docker required)

First add the testcontainers deps (not yet in `go.mod`):

```bash
cd src/services/notifications
go get github.com/testcontainers/testcontainers-go@v0.34.0
go get github.com/testcontainers/testcontainers-go/modules/postgres@v0.34.0
go get github.com/testcontainers/testcontainers-go/modules/kafka@v0.34.0
go mod tidy
```

Make sure the Docker daemon is running, then:

```bash
# both integration tests
go test -tags=integration ./...

# postgres only
go test -tags=integration -run TestDeviceTokenLifecycle ./internal/postgres/
go test -tags=integration -v ./internal/postgres/

# kafka end-to-end pipeline (produce → consumer → dispatcher → user channel)
go test -tags=integration -v -run TestConsumerPipeline ./internal/kafka/
```

The first run pulls images (`postgres:16-alpine`,
`confluentinc/confluent-local:7.5.0`); allow extra time. The Kafka pipeline test
has generous timeouts because broker startup + first produce can be slow.

> **Schema note:** the Postgres test creates `device_tokens` and
> `sent_notifications` itself (see `schema` in the test) because the service has
> no migration tooling yet. If/when migrations are added, point the test at them
> instead.

## What each layer covers

- **mapper** — register/send/unregister correctness, non-blocking drop when the
  buffer is full, duplicate-connection takeover, the stale-unregister guard
  (a late `Unregister` from an old connection must not kill the new one), and
  race-tested concurrent access.
- **dispatcher** — the full routing table (incl. `ride_cancelled` → other
  party), idempotency dedup, "push failure still marks processed / no replay",
  error propagation from `IsProcessed`, unknown event types, malformed payloads.
- **streamer** — envelope → thin DTO conversion with raw-payload passthrough.
- **notifier** — push title/body/priority mapping for every event type, the
  display-name fallback, and unknown-event "no push".
- **handler** — 401 without `X-User-Id`, SSE headers + `retry:` line, the
  `event:`/`data:` wire framing, and unregister-on-disconnect.
- **postgres (integration)** — device-token CRUD + upsert conflict, idempotency
  mark/check + double-mark no-op, retention cleanup.
- **kafka (integration)** — the whole inbound path against a real broker.
```