# PullApp — Metrics Design

## Conventions

- **counter** — monotonicznie rosnący, zawsze `_total` suffix
- **histogram** — rozkład wartości (latency, duration), daje p50/p95/p99
- **gauge** — wartość w danej chwili (może rosnąć i maleć)
- **Labels** — nigdy wysokiej kardynalności (nie: ride_id, user_id). Tylko skończone zbiory wartości.
- **Timestamp-based durations** — przy publish zapisuj `job:{id}:enqueued_at` do Redis (TTL 5min), przy consume licz różnicę i recorduj histogram, kasuj klucz.

---

## Matching Pipeline

Mierzony w: **Trip Planner**

```
matching_requests_total
  typ: counter
  labels: status=(queued|failed_validation|no_area_coverage)
  znaczenie: ile requestów matchingu wchodzi do systemu

matching_queue_duration_seconds
  typ: histogram
  labels: result=(success|timeout|error|no_drivers)
  znaczenie: czas od publish do ComputeQueue do odebrania z ResultsQueue
  implementacja: timestamp w Redis przy publish, diff przy consume
  cel: p95 < 3s

matching_result_total
  typ: counter
  labels: result=(matched|no_drivers|timeout|error)
  znaczenie: finalne wyniki matchingu
```

---

## Ride Lifecycle

Mierzony w: **Trip Planner**

```
ride_transitions_total
  typ: counter
  labels: from_state, to_state, reason
  stany: requested, matched, driver_notified, accepted, declined, started, completed, cancelled
  reason: driver_accepted, driver_declined, driver_timeout, passenger_cancelled, system_error
  znaczenie: pełna mapa przepływu przejazdów

ride_active
  typ: gauge
  znaczenie: ile przejazdów jest aktualnie aktywnych (stany: accepted, started)

ride_cancelled_total
  typ: counter
  labels: cancelled_by=(passenger|driver|system), stage=(before_match|after_match|during_ride)
  znaczenie: anulowania z podziałem na etap i inicjatora

ride_driver_decline_total
  typ: counter
  labels: reason=(explicit|timeout|no_response)
  znaczenie: odmowy kierowców
```

---

## Driver

Mierzony w: **Trip Planner**

```
driver_online
  typ: gauge
  znaczenie: ilu kierowców jest online w tej chwili (odczyt z Redis)
  alert: < threshold = niedobór podaży

driver_route_registrations_total
  typ: counter
  labels: status=(queued|completed|failed)
  znaczenie: rejestracje tras kierowców

```

---

## Compute Queue / Route-Calc

Mierzony w: **Trip Planner** (queue depth z RabbitMQ Management API), **Route-Calc** (czas obliczeń)

```
compute_queue_depth
  typ: gauge
  źródło: RabbitMQ Management API scrape
  znaczenie: ile jobów czeka w ComputeQueue
  alert: > 50 przez > 30s = KEDA nie nadąża skalować

results_queue_depth
  typ: gauge
  znaczenie: ile wyników czeka nieodebranych (powinno być ~0)
  alert: > 0 przez > 60s = Trip Planner nie odbiera wyników

route_calc_duration_seconds
  typ: histogram
  labels: job_type=(route_registration|passenger_match), result=(success|error)
  znaczenie: czas obliczeń w Route-Calc
  implementacja: diff timestamp publish→receive
  cel: p95 < 2s

route_calc_workers_active
  typ: gauge
  źródło: kube-state-metrics (deployment replicas)
  znaczenie: ile instancji Route-Calc działa (z KEDA)
```

---

## Notifications

Mierzony w: **Notification Service**

```
notifications_sent_total
  typ: counter
  labels: channel=(push_fcm|sms|email), status=(success|failed), event_type=(match_found|driver_accepted|ride_started|ride_completed)
  znaczenie: throughput notyfikacji per kanał

notification_delivery_duration_seconds
  typ: histogram
  labels: channel=(push_fcm|sms|email)
  znaczenie: czas od odebrania eventu z Kafka do wysłania przez zewnętrzny provider

notification_kafka_lag
  typ: gauge
  znaczenie: jak daleko Notification Service jest za producentami na Kafce
  alert: > 1000 = serwis nie nadąża
```

---

## API Gateway

```
gateway_requests_total
  typ: counter
  labels: service, method, status_code
  znaczenie: ruch per serwis docelowy

gateway_request_duration_seconds
  typ: histogram
  labels: service, method
  znaczenie: latency na poziomie gateway

gateway_rate_limited_total
  typ: counter
  labels: reason=(per_user|per_ip)
  znaczenie: ile requestów odrzucono przez rate limiting

gateway_auth_failures_total
  typ: counter
  labels: reason=(invalid_token|expired_token|missing_token)
  znaczenie: problemy z autentykacją
```

---

## Alerty — priorytety

### P0 — natychmiastowe działanie
```
driver_online < 5                                    # brak kierowców
compute_queue_depth > 100 przez 60s                  # kolejka się zapycha
matching_no_drivers_found_total rate > 10/min        # pasażerowie bez odpowiedzi
ride_active anomaly (nagły spadek do 0)              # system przestał działać
```

### P1 — wymaga uwagi
```
matching_queue_duration_seconds p95 > 5s             # matching wolny
ride_acceptance_duration_seconds p95 > 120s          # kierowcy wolno reagują
notification_kafka_lag > 1000                        # notyfikacje się opóźniają
results_queue_depth > 0 przez > 60s                  # Trip Planner nie odbiera wyników
```

---

## Implementacja — gdzie co mierzyć

| Metryka | Serwis | Metoda |
|---|---|---|
| matching_* | Trip Planner | Redis timestamp diff |
| ride_* | Trip Planner | state machine hooks |
| driver_online | Trip Planner | Redis SET count |
| driver_route_* | Trip Planner | Redis timestamp diff |
| compute_queue_depth, results_queue_depth | Trip Planner | RabbitMQ Management API |
| route_calc_* | Route-Calc / Trip Planner | OTel SDK + Redis diff |
| notifications_* | Notification Service | Kafka consumer + provider callback |
| gateway_* | API Gateway (YARP) | middleware |
