# PullApp — Monitoring & Grafana Design

## Zasady ogólne

- Każdy dashboard odpowiada na **jedno pytanie** — nie mieszaj infrastruktury z biznesem
- Górny rząd zawsze **stat panels** — szybki status na pierwszy rzut oka
- Każdy dashboard ma **panel Loki z błędami** — surowe logi level=error z danego kontekstu
- Dashboardy linkują do siebie nawzajem — anomalia w biznesie → Request Flow → Loki → Tempo
- Wszystkie dashboardy mają **zmienną `$environment`** (local/staging/production) i **`$time_range`**
- Docelowy format provisioning: JSON w ConfigMapie K8s, ładowany automatycznie przez Grafanę

---

## Dashboard 1 — System Health
*Pytanie: Czy infrastruktura działa?*
*Odbiorcy: DevOps, on-call*

### Row 1: Status (stat panels)
```
[Pods Running]          [Pods Restarting]       [CPU Usage Cluster]     [Memory Usage Cluster]
gauge: suma ready pods  gauge: restarty 1h       gauge: %                gauge: %
kolor: zielony/czerwony alert: > 0               alert: > 80%            alert: > 85%
```

### Row 2: Per-Service Health (table)
```
[Service Status Table]
kolumny: Service | Namespace | Ready | Restarts (1h) | CPU % | Memory % | Last Restart
źródło: kube-state-metrics
sortowanie: po restartach malejąco
```

### Row 3: Queues
```
[ComputeQueue Depth]            [ResultsQueue Depth]            [RabbitMQ Connections]
gauge: compute_queue_depth      gauge: results_queue_depth      stat
alert: > 50                     alert: > 10                     
time series pod gaugem          time series pod gaugem
```

### Row 4: Databases
```
[Postgres Connections]          [Redis Memory]                  [Redis Hit Rate]
time series: active/idle/max    gauge: used/max MB              gauge: %
                                alert: > 80%                    alert: < 90%
```

### Row 5: Errors (Loki)
```
[Error Logs — All Services]
panel type: logs
query: {namespace="pullapp"} |= "error" | level="error"
limit: 50 ostatnich
```

---

## Dashboard 2 — Request Flow
*Pytanie: Gdzie są wąskie gardła i błędy?*
*Odbiorcy: Backend developers, on-call*

### Row 1: Traffic Overview (stat)
```
[Total RPS]             [Error Rate %]          [p95 Latency]           [Active Connections]
stat: suma              stat: 5xx/total         stat: max p95            stat: WebSocket
alert: spike > 2x avg   alert: > 1%             alert: > 500ms
```

### Row 2: Request Rate per Service
```
[Requests/sec — All Services]
time series: jedna linia per service
gateway_requests_total rate
legenda: trip-planner, accounts, payments, chat, notifications
```

### Row 3: Error Rate per Service
```
[HTTP 5xx Rate]                             [gRPC Error Rate]
time series: per service, stacked           time series: per service
źródło: gateway_requests_total{status=~"5.."}
```

### Row 4: Latency
```
[p50 Latency per Service]       [p95 Latency per Service]       [p99 Latency per Service]
time series                     time series                     time series
```

### Row 5: Slowest Endpoints (table)
```
[Top 10 Slowest Endpoints]
kolumny: Service | Endpoint | Method | p95 (ms) | RPS | Error %
sortowanie: p95 malejąco
źródło: gateway_request_duration_seconds histogram
```

### Row 6: Gateway
```
[Rate Limited Requests]         [Auth Failures]
time series                     time series
labels: reason                  labels: reason
```

### Row 7: Errors (Loki)
```
[Error Logs z trace_id]
query: {namespace="pullapp"} | json | level="error"
pokaż: timestamp, service, message, trace_id (jako link do Tempo)
```

---

## Dashboard 3 — Ride Funnel
*Pytanie: Czy biznes działa?*
*Odbiorcy: Product, Tech Lead, on-call*

### Row 1: Funnel (stat panels w jednej linii)
```
[Requests]      →       [Matched]       →       [Accepted]      →       [Started]       →       [Completed]
counter rate            counter rate            counter rate            counter rate            counter rate
                        konwersja %             konwersja %             konwersja %             konwersja %
```
Konwersja liczona jako: `rate(matched) / rate(requests)` itp. Kolor czerwony jeśli < threshold.

### Row 2: Matching Performance
```
[Matching Duration p95]                     [No Drivers Found Rate]
gauge: matching_queue_duration p95          time series: rate per min
cel wyświetlony: < 3s                       alert: > 5/min
kolor: zielony<2s / żółty<5s / czerwony>5s
```

```
[Candidates Evaluated]                      [Route-Calc Workers]
histogram heatmap                           time series: replicas
matching_candidates_evaluated               źródło: kube-state-metrics
```

### Row 3: Driver Behavior
```
[Drivers Online]                [Acceptance Rate]               [Decline Reasons]
gauge: driver_online            gauge: accepted/(accepted+declined)   bar chart: labels reason
big stat, zielony/czerwony      alert: < 70%                    
time series pod                 time series pod
```

```
[Acceptance Response Time p95]
time series: ride_acceptance_duration_seconds p95
cel: < 60s
```

### Row 4: Ride States
```
[Active Rides]                              [State Transition Heatmap]
gauge: ride_active                          heatmap: ride_state_duration_seconds
time series pod                             oś x: from_state→to_state
```

```
[Cancellations by Stage]                    [Cancellations by Actor]
time series stacked                         pie chart
labels: stage                               labels: cancelled_by
```

### Row 5: GPS & Driver Tracker
```
[GPS Update Rate]                           [Position Staleness p95]
time series: driver_gps_updates_total       gauge: staleness histogram p95
                                            alert: > 30s
```

### Row 6: Errors (Loki — tylko Trip Planner)
```
[Trip Planner Errors]
query: {service_name="trip-planner"} | json | level="error"
```

---

## Dashboard 4 — Notifications & Events
*Pytanie: Czy użytkownicy dostają powiadomienia?*

### Row 1: Stats
```
[Sent/min]      [Failed/min]    [Kafka Lag]     [Delivery p95]
```

### Row 2: Per Channel
```
[Push FCM]                      [SMS]                           [Email]
time series: sent/failed        time series: sent/failed        time series: sent/failed
```

### Row 3: Per Event Type
```
[Notifications by Event]
bar chart: labels event_type (match_found, driver_accepted, ride_started, ride_completed)
```

### Row 4: Kafka
```
[Consumer Lag]                              [Processing Duration]
time series: notification_kafka_lag         histogram: notification_delivery_duration
alert: > 1000
```

---

## Linkowanie między dashboardami

```
System Health
    → kliknięcie w service → Request Flow (filtr na service)
    
Request Flow  
    → kliknięcie w error log → Explore Loki (filtr na trace_id)
    → trace_id w logu → Tempo (pełny waterfall)
    
Ride Funnel
    → anomalia → Request Flow (filtr na trip-planner)
    → błędy → Loki → Tempo
```

Implementacja: Grafana **data links** na panelach — `${__data.fields.trace_id}` jako parametr URL do Tempo.

---

## Provisioning w K8s

```
k8s/monitoring/grafana/
  datasources-configmap.yaml      # Prometheus + Loki + Tempo z correlation config
  dashboards-configmap.yaml       # referencja do folderów
  dashboards/
    system-health.json
    request-flow.json
    ride-funnel.json
    notifications.json
```

Grafana ładuje automatycznie przez:
```yaml
# w kube-prometheus-stack values.yaml
grafana:
  sidecar:
    dashboards:
      enabled: true
      label: grafana_dashboard
      searchNamespace: monitoring
```

Każdy ConfigMap z dashboardem musi mieć label `grafana_dashboard: "1"`.

---

## Zmienne globalne dashboardów

Każdy dashboard definiuje te zmienne:

```
$environment    — dropdown: local | staging | production
                  filtruje: deployment.environment label
                  
$service        — multi-select: wszystkie serwisy pullapp
                  filtruje: service_name label
                  
$interval       — auto interval dla time series
                  wartości: 30s, 1m, 5m, 15m
```

---

## Alerting (przyszłość)

Alerty definiowane jako Grafana Alerting rules, nie osobne pliki Prometheus.
Każdy alert ma:
- **summary**: co się stało
- **description**: jak zdiagnozować (link do dashboardu)
- **runbook_url**: link do dokumentacji

Routing: P0 → PagerDuty/telefon, P1 → Slack #alerts, P2 → Slack #monitoring-info
