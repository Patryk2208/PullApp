# Plan: Grafana Dashboards

## Context

Metryki są zaimplementowane i eksportowane przez OTel Collector do Prometheus. Grafana jest już zainstalowana przez `kube-prometheus-stack` z sidecar autodiscovery — każdy ConfigMap z labelem `grafana_dashboard: "1"` w namespace `monitoring` zostaje automatycznie załadowany.

---

## ⚠️ OTel → Prometheus naming gotcha

OTel SDK zmienia nazwy przy eksporcie do Prometheus:
- **Kropki → podkreślniki**: `trip_planner.routes.registered` → `trip_planner_routes_registered`
- **Countery**: dodaje `_total` suffix jeśli go nie ma → `trip_planner_routes_registered_total`
- **Histogramy**: generuje `_bucket`, `_sum`, `_count` variants
- **UpDownCounter / Gauge**: nazwa bez zmian → `ride_active`

Pełna tabela nazw w Prometheus:

| Kod (OTel name) | Prometheus name |
|---|---|
| `matching_requests_total` | `matching_requests_total` |
| `matching_result_total` | `matching_result_total` |
| `matching_no_drivers_found_total` | `matching_no_drivers_found_total` |
| `matching_queue_duration_seconds` | `matching_queue_duration_seconds_bucket/sum/count` |
| `ride_active` | `ride_active` |
| `ride_transitions_total` | `ride_transitions_total` |
| `ride_acceptance_duration_seconds` | `ride_acceptance_duration_seconds_bucket/sum/count` |
| `ride_driver_decline_total` | `ride_driver_decline_total` |
| `gateway_requests_total` | `gateway_requests_total` |
| `gateway_request_duration_seconds` | `gateway_request_duration_seconds_bucket/sum/count` |
| `accounts.login_duration_seconds` | `accounts_login_duration_seconds_bucket/sum/count` |
| `accounts.login.success` | `accounts_login_success_total` |
| `accounts.login.failed` | `accounts_login_failed_total` |
| `trip_planner.routes.registered` | `trip_planner_routes_registered_total` |
| `route_calc.jobs.processed` | `route_calc_jobs_processed_total` |
| `route_calc.duration.seconds` | `route_calc_duration_seconds_bucket/sum/count` |

**Zweryfikuj przed pisaniem queries** — patrz krok 2 poniżej.

---

## Krok 1 — Dostęp do Grafany

```bash
kubectl port-forward svc/kube-prometheus-stack-grafana 3000:80 -n monitoring
```

Otwórz `http://localhost:3000` → **admin / pullapp-grafana**

Sprawdź że dane płyną: **Explore → Prometheus** → wpisz `{__name__=~"matching.*"}` → powinieneś zobaczyć metryki.

## Krok 2 — Weryfikacja nazw metryk

W **Explore → Prometheus** → przełącz na **Metrics browser** i wyszukaj:
- `matching_requests` — powinno pokazać `matching_requests_total`
- `ride_active` — bez sufixu
- `gateway_request_duration` — powinno pokazać `_bucket`, `_sum`, `_count`

Jeśli nazwy się różnią od tabeli wyżej — zaktualizuj queries w planach poniżej.

---

## Dashboardy do zbudowania

### Dashboard 1 — System Health
*Pytanie: Czy infrastruktura działa?*

#### Panele i queries

**Row 1 — Stat panels (górny rząd)**

| Panel | Typ | Query |
|---|---|---|
| Active Rides | stat | `ride_active` |
| Error Rate | stat | `sum(rate(gateway_requests_total{status_code=~"5.."}[5m])) / sum(rate(gateway_requests_total[5m])) * 100` |
| Pods Running | stat | `count(kube_pod_status_ready{namespace="pullapp", condition="true"})` |
| Pod Restarts (1h) | stat | `sum(increase(kube_pod_container_status_restarts_total{namespace="pullapp"}[1h]))` |

**Row 2 — Per-service status (table)**
```promql
# CPU per pod
rate(container_cpu_usage_seconds_total{namespace="pullapp", container!=""}[5m])

# Memory per pod
container_memory_working_set_bytes{namespace="pullapp", container!=""}
```
Table z kolumnami: Service | CPU % | Memory MB | Restarts (1h)

**Row 3 — Queues**
```promql
# RabbitMQ compute queue depth (jeśli RabbitMQ eksportuje metryki)
rabbitmq_queue_messages{queue="compute"}

# Route-calc workers (replicas)
kube_deployment_status_replicas_ready{deployment="route-calc", namespace="pullapp"}
```

**Row 4 — Error logs (Loki)**
```logql
{namespace="pullapp"} | json | level="error"
```

---

### Dashboard 2 — Request Flow
*Pytanie: Gdzie są wąskie gardła i błędy?*

#### Panele i queries

**Row 1 — Traffic overview (stat)**
```promql
# Total RPS
sum(rate(gateway_requests_total[1m]))

# Error rate %
sum(rate(gateway_requests_total{status_code=~"5.."}[1m])) / sum(rate(gateway_requests_total[1m])) * 100

# p95 latency (ms)
histogram_quantile(0.95, sum by(le) (rate(gateway_request_duration_seconds_bucket[5m]))) * 1000
```

**Row 2 — Request rate per service (time series)**
```promql
sum by(service) (rate(gateway_requests_total[1m]))
```
Legenda: `{{service}}`

**Row 3 — Latency per service**
```promql
# p50
histogram_quantile(0.50, sum by(le, service) (rate(gateway_request_duration_seconds_bucket[5m])))

# p95
histogram_quantile(0.95, sum by(le, service) (rate(gateway_request_duration_seconds_bucket[5m])))

# p99
histogram_quantile(0.99, sum by(le, service) (rate(gateway_request_duration_seconds_bucket[5m])))
```

**Row 4 — Auth (accounts)**
```promql
# Login success rate
rate(accounts_login_success_total[1m])

# Login fail rate
rate(accounts_login_failed_total[1m])

# Login duration p95 (ms)
histogram_quantile(0.95, sum by(le) (rate(accounts_login_duration_seconds_bucket[5m]))) * 1000
```

**Row 5 — Error logs z trace_id (Loki)**
```logql
{namespace="pullapp"} | json | level="error" | line_format "{{.service_name}} — {{.message}} trace={{.trace_id}}"
```

---

### Dashboard 3 — Ride Funnel
*Pytanie: Czy biznes działa?*

#### Panele i queries

**Row 1 — Funnel (stat panels w jednej linii)**
```promql
# Requests złożone
rate(matching_requests_total{status="queued"}[5m])

# Matched
rate(matching_result_total{result="matched"}[5m])

# Accepted (MatchConfirmed)
rate(trip_planner_match_confirmed_total[5m])

# Completed
rate(trip_planner_rides_completed_total[5m])

# No drivers (alert metric)
rate(matching_no_drivers_found_total[5m])
```

**Row 2 — Active rides gauge**
```promql
ride_active
```
Typ: stat z kolorami (zielony > 0, szary = 0)

**Row 3 — Matching performance**
```promql
# Matching queue duration p95 (s)
histogram_quantile(0.95, sum by(le) (rate(matching_queue_duration_seconds_bucket[5m])))

# Matching queue duration p50 (s)
histogram_quantile(0.50, sum by(le) (rate(matching_queue_duration_seconds_bucket[5m])))

# No drivers rate
rate(matching_no_drivers_found_total[5m])

# Matching results breakdown (time series stacked)
sum by(result) (rate(matching_result_total[5m]))
```

**Row 4 — Driver behavior**
```promql
# Acceptance duration p95 (s)
histogram_quantile(0.95, sum by(le) (rate(ride_acceptance_duration_seconds_bucket[5m])))

# Declines by reason
sum by(reason) (rate(ride_driver_decline_total[5m]))

# Accept vs decline ratio
rate(trip_planner_match_confirmed_total[5m]) / (rate(trip_planner_match_confirmed_total[5m]) + rate(trip_planner_match_declined_total[5m]))
```

**Row 5 — Ride state transitions (time series)**
```promql
# Wszystkie tranzycje (stacked)
sum by(from_state, to_state) (rate(ride_transitions_total[5m]))

# Cancellations by actor
sum by(cancelled_by) (rate(trip_planner_rides_cancelled_total[5m]))
```

**Row 6 — Route-calc performance**
```promql
# Duration p95 by job type
histogram_quantile(0.95, sum by(le, job_type) (rate(route_calc_duration_seconds_bucket[5m])))

# Jobs processed vs failed
rate(route_calc_jobs_processed_total[5m])
rate(route_calc_jobs_failed_total[5m])
```

**Row 7 — Trip planner errors (Loki)**
```logql
{service_name="trip-planner"} | json | level="error"
```

---

## Instrukcja manualna — budowanie dashboardu w Grafanie

### Tworzenie dashboardu

1. **Dashboards → New → New Dashboard**
2. Kliknij **Add visualization**
3. Wybierz data source: **Prometheus**

### Dodawanie panelu

1. W polu **Metrics browser** wklej query z planu wyżej
2. Przełącz na **Code** (nie Builder) jeśli query jest złożone
3. W **Panel options** (prawy sidebar) ustaw:
   - **Title**: nazwa panelu
   - **Panel type**: Time series / Stat / Gauge / Table / Logs
4. Dla stat paneli → **Value options → Calculation: Last***
5. Dla time series → **Legend: `{{label_name}}`** żeby rozróżniać serie

### Konfiguracja stat panelu z progiem koloru

1. Typ: **Stat**
2. **Thresholds** (prawy sidebar):
   - Kliknij **+ Add threshold**
   - np. dla error rate: Base=green, 1=yellow, 5=red
3. **Color mode: Background** dla wyraźnego sygnału

### Konfiguracja histogramu (p95/p50)

Query pattern:
```promql
histogram_quantile(0.95, sum by(le) (rate(METRIC_bucket[5m])))
```
- `sum by(le)` — agreguje wszystkie instancje
- `sum by(le, label)` — rozbija po labelu (np. `service`, `job_type`)
- Czas: ustaw **Min step** na `15s` żeby dopasować do interwału OTel Collectora

### Panel Loki (logi)

1. Typ: **Logs**
2. Data source: **Loki** (nie Prometheus!)
3. Wklej query LogQL z planu
4. **Visualize labels**: zaznacz `level`, `service_name`, `trace_id`

### Eksport JSON

1. Kliknij ikonę **⚙️ (Dashboard settings)** → górny pasek
2. **JSON Model** → Ctrl+A, Ctrl+C (skopiuj cały JSON)
   **LUB**: settings → **Save dashboard** → potem **Share → Export → Save to file**

---

## Struktura ConfigMap w K8s

Plik: `src/infrastructure/k8s/monitoring/grafana/dashboards/ride-funnel-configmap.yaml`

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: grafana-dashboard-ride-funnel
  namespace: monitoring
  labels:
    grafana_dashboard: "1"        # sidecar autodiscovery trigger
data:
  ride-funnel.json: |
    {
      "__inputs": [],
      "__requires": [],
      "title": "Ride Funnel",
      "uid": "ride-funnel",
      ... (wklejony JSON z Grafany)
    }
```

### Ważne przy wklejaniu JSON

W eksportowanym JSON Grafana wstawia `${DS_PROMETHEUS}` zamiast nazwy data source. Zamień na nazwę data source jak jest skonfigurowana w klastrze:

```bash
# Znajdź nazwę data source
kubectl get configmap -n monitoring kube-prometheus-stack-grafana-datasource -o yaml | grep name
```

Zwykle jest to `Prometheus` lub `prometheus`.

---

## Struktura plików

```
src/infrastructure/k8s/monitoring/
  grafana/
    dashboards/
      system-health-configmap.yaml
      request-flow-configmap.yaml
      ride-funnel-configmap.yaml
```

Każdy plik to osobny ConfigMap. Apply:

```bash
kubectl apply -f src/infrastructure/k8s/monitoring/grafana/dashboards/
```

Grafana załaduje dashboard automatycznie w ciągu ~30s (sidecar polling interval).

---

## Kolejność pracy

1. `kubectl port-forward` → Grafana dostępna lokalnie
2. Verify metric names w Prometheus Explore
3. Zbuduj Dashboard 3 (Ride Funnel) — najważniejszy biznesowo
4. Zbuduj Dashboard 2 (Request Flow) — latency/errors
5. Zbuduj Dashboard 1 (System Health) — infra
6. Eksportuj JSON → wklej do ConfigMapów → `kubectl apply`
7. Commit do repo
