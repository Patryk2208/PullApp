# Metrics

The metrics **actually emitted** by the running services, with both the code-side
instrument name and the name they land under in Prometheus. This supersedes the old
metrics *design* doc — these are verified against the deployed dashboards.

## OTel → Prometheus naming

The OTLP→Prometheus path rewrites names. Rules that bite here:

- **dots → underscores**: `accounts.login.success` → `accounts_login_success…`
- **unit suffix appended**: an instrument with unit `rides` / `attempts` /
  `declines` gets that word in the name → `ride_active` (unit `rides`) becomes
  **`ride_active_rides`**.
- **counters get `_total`** (after the unit): `ride_cancelled_total` (unit `rides`)
  → `ride_cancelled_rides_total`.
- **histograms** expand to `_bucket` / `_sum` / `_count`.

So the *code* name is not the *query* name — always use the Prometheus column below.

## Trip Planner — meter `TripPlanner`

| Code instrument | Type | Prometheus name | Labels |
|-----------------|------|-----------------|--------|
| `matching_requests_total` | counter | `matching_requests_total` | `status` |
| `matching_queue_duration_seconds` | histogram | `matching_queue_duration_seconds_*` | `result` |
| `matching_result_total` | counter | `matching_result_results_total` | `result` |
| `ride_transitions_total` | counter | `ride_transitions_total` | `from_state`, `to_state`, `reason` |
| `ride_active` | up/down counter | `ride_active_rides` | — |
| `ride_cancelled_total` | counter | `ride_cancelled_rides_total` | `cancelled_by`, `stage` |
| `ride_driver_decline_total` | counter | `ride_driver_decline_declines_total` | `reason` |
| `driver_route_registrations_total` | counter | `driver_route_registrations_total` | `status` |
| `route_calc_duration_seconds` | histogram | `route_calc_duration_seconds_*` | `job_type`, `result` |

> **Not yet wired** (flagged for backend): `reject` doesn't increment the decline
> counter and `cancel` doesn't increment the cancelled counter, so those series stay
> flat in practice.

## Accounts — meter `Accounts`

| Code instrument | Prometheus name |
|-----------------|-----------------|
| `accounts.registrations` | `accounts_registrations_users_total` |
| `accounts.login.success` | `accounts_login_success_attempts_total` |
| `accounts.login.failed` | `accounts_login_failed_attempts_total` |
| `accounts.validation.failures` | `accounts_validation_failures_total` |
| `accounts.login_duration_seconds` | `accounts_login_duration_seconds_*` |

## Gateway — meter `Gateway`

| Code instrument | Prometheus name | Labels |
|-----------------|-----------------|--------|
| `gateway_requests_total` | `gateway_requests_total` | `service`, `method`, `status_code` |
| `gateway_request_duration_seconds` | `gateway_request_duration_seconds_*` | `service`, `method` |

Plus standard ASP.NET Core instrumentation: `http_server_request_duration_seconds_*`,
`aspnetcore_diagnostics_exceptions_total`. The custom counters are **pre-seeded with
zero series** for every `service × method × status` combination at startup, so
panels have data immediately.

## Route-Calc — Python OTel

| Code instrument | Prometheus name |
|-----------------|-----------------|
| `route_calc.jobs.processed` | `route_calc_jobs_processed_total` |
| `route_calc.jobs.failed` | `route_calc_jobs_failed_total` |
| `route_calc.duration.seconds` | `route_calc_duration_seconds_*` |

> **KEDA caveat:** route-calc scales to zero between jobs, so `route_calc_*` series
> are **ephemeral** — they disappear from Prometheus when no pod is running. Expect
> gaps, not a continuous line.

## Notifications

`notifications_sent_total`, `notification_kafka_lag_messages` are referenced by the
dashboard; they populate only once the Kafka→push path is built (currently SSE-only).

## Infra / cluster (kube-prometheus-stack)

Dashboards also use cluster metrics from kube-state-metrics / cAdvisor:
`kube_pod_status_ready`, `kube_pod_container_status_restarts_total`,
`kube_deployment_status_replicas_ready`, `container_cpu_usage_seconds_total`,
`container_memory_working_set_bytes`.

> **Deliberately excluded:** there are **no database/Redis/RabbitMQ exporter
> metrics** in the dashboards. The decision was to dashboard only code-emitted
> metrics + logs, not stand up infra exporters.
