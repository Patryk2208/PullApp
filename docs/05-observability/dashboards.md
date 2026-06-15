# Grafana Dashboards

Three dashboards are provisioned into Grafana. They are built **only on
code-emitted metrics + logs** (and cluster metrics from kube-state-metrics /
cAdvisor) — no database/Redis/RabbitMQ exporters. See [metrics.md](metrics.md) for
the underlying series.

## Provisioning

Source: `src/infrastructure/k8s/observability/grafana/dashboards/`

```
system-health.json   + system-health-configmap.yaml
request-flow.json    + request-flow-configmap.yaml
ride-funnel.json     + ride-funnel-configmap.yaml
```

Each dashboard ships as a ConfigMap labelled **`grafana_dashboard: "1"`** in the
`monitoring` namespace; the kube-prometheus-stack Grafana **sidecar auto-discovers**
and loads it (~30 s). No manual import.

```bash
kubectl apply -f src/infrastructure/k8s/observability/grafana/dashboards/
kubectl port-forward svc/kube-prometheus-stack-grafana 3000:80 -n monitoring
# http://localhost:3000  — admin / pullapp-grafana
```

## 1 — System Health
*Is the infrastructure up?* (DevOps / on-call)

- Stat row: active rides (`ride_active_rides`), error rate
  (`gateway_requests_total{status_code=~"5.."}`), pods ready
  (`kube_pod_status_ready`), restarts 1h (`kube_pod_container_status_restarts_total`).
- Per-pod CPU / memory (`container_cpu_usage_seconds_total`,
  `container_memory_working_set_bytes`).
- route-calc replicas (`kube_deployment_status_replicas_ready`).
- Error logs panel (Loki): `{namespace="pullapp"} | json | level="error"`.

> The original design had Postgres/Redis/RabbitMQ rows — these were **removed**;
> the dashboard uses code + cluster metrics only.

## 2 — Request Flow
*Where are the bottlenecks and errors?* (backend / on-call)

- Traffic + error rate + p95 latency from `gateway_requests_total` /
  `gateway_request_duration_seconds_bucket`.
- Request rate and latency **per target service** (`by(service)`).
- **Auth panel** (accounts): `accounts_login_success_attempts_total` /
  `accounts_login_failed_attempts_total` rates + login-duration p95. *(This panel was
  fixed to the real `accounts_*_attempts_total` names — the design doc's guessed
  names returned no data.)*
- Error logs with `trace_id` (Loki → Tempo link).

## 3 — Ride Funnel
*Is the business working?* (product / tech lead)

- Funnel: requests (`matching_requests_total`) → matched
  (`matching_result_results_total{result="matched"}`) → active (`ride_active_rides`).
- Matching performance: `matching_queue_duration_seconds` p50/p95, results breakdown.
- Driver behaviour: declines (`ride_driver_decline_declines_total` by `reason`),
  cancellations (`ride_cancelled_rides_total`), transitions (`ride_transitions_total`
  by `from_state`/`to_state`).
- route-calc: `route_calc_duration_seconds` p95, `route_calc_jobs_processed_total`.
- Trip Planner error logs (Loki).

## Expect some empty panels

A few panels legitimately show **No data** until the corresponding path is exercised
or built:
- `route_calc_*` — only present while a route-calc pod is alive (KEDA scale-to-zero).
- decline / cancel counters — not yet incremented by the reject/cancel handlers.
- notifications panels — populate once the Kafka→push path exists (SSE-only today).

This is expected given the implementation status, not a broken dashboard.
