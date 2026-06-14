# Observability

PullApp uses the OpenTelemetry (OTLP) approach: every service exports **traces, metrics, and logs** to a central **OTel Collector**, which fans out to **Tempo** (traces), **Prometheus** (metrics), and **Loki** (logs). **Grafana** is the single pane of glass for all three.

```
services ──OTLP gRPC──► otel-collector ──► Tempo      (traces)
                                       ──► Prometheus  (metrics, via remote-write)
                                       ──► Loki        (logs, via otlphttp exporter)
                                               │
                                           Grafana
```

> Instrumented services: **gateway, accounts, trip-planner** (.NET OTel SDK) and
> **route-calc** (Python OTel SDK). The frontend is **not** instrumented — browser
> telemetry is out of scope.
>
> The collector's **logs pipeline uses the `otlphttp/loki` exporter**
> (`…/otlp/v1/logs`). This is load-bearing: removing it makes route-calc's OTLP log
> export fail with `StatusCode.UNIMPLEMENTED` (it has nowhere to send logs).
>
> See [metrics.md](metrics.md) for the exact metrics each service emits and
> [dashboards.md](dashboards.md) for the three provisioned Grafana dashboards.

## Stack components

| Component | Helm chart | Namespace |
|---|---|---|
| Prometheus + Grafana | `prometheus-community/kube-prometheus-stack` | `monitoring` |
| Loki (single binary) | `grafana/loki` | `monitoring` |
| Tempo (single binary) | `grafana/tempo` | `monitoring` |
| OTel Collector | `open-telemetry/opentelemetry-collector` (contrib) | `monitoring` |

## Install

```bash
cd src/infrastructure/k8s/observability
chmod +x install.sh
./install.sh
```

The script adds the required Helm repos, creates the `monitoring` namespace, and installs all four charts with pinned versions. It takes ~5 minutes on a fresh cluster.

## Access Grafana

```bash
kubectl port-forward svc/kube-prometheus-stack-grafana 3000:80 -n monitoring
```

Open `http://localhost:3000`. Default credentials: **admin / pullapp-grafana**.

The following data sources are pre-configured:
- **Prometheus** — default data source for metrics
- **Loki** — logs from all services
- **Tempo** — distributed traces, linked to Loki via `trace → logs` correlation

## Visualising data

### Logs (Loki)
Go to **Explore → Loki** and query by service name:
```logql
{job="gateway"} |= "error"
{job="trip-planner"}
```

### Metrics (Prometheus)
Go to **Explore → Prometheus**. Useful queries:
```promql
# HTTP request rate per service
rate(http_server_request_duration_seconds_count[1m])

# .NET GC heap
process_runtime_dotnet_gc_heap_size_bytes

# route-calc jobs
rate(route_calc_jobs_processed_total[1m])
```

### Traces (Tempo)
Go to **Explore → Tempo → Search**. Filter by service name (`gateway`, `trip-planner`, `accounts`, `route-calc`). Click a trace to see the waterfall and follow the `Logs for this span` link to Loki.

## Service instrumentation summary

### .NET services (gateway, trip-planner, accounts)

Uses the official OpenTelemetry .NET SDK (`OpenTelemetry.Extensions.Hosting` + instrumentation packages).  
Each service `Program.cs` calls:
```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("<service-name>"))
    .WithTracing(...)
    .WithMetrics(...);
builder.Logging.AddOpenTelemetry(...);
```
Instrumentation covers: ASP.NET Core request spans, HttpClient spans, .NET runtime metrics, and all structured log output.

The OTLP endpoint is injected via `OTEL_EXPORTER_OTLP_ENDPOINT` env var in each deployment (defaults to the in-cluster collector address).

### route-calc (Python)

Uses `opentelemetry-sdk` + `opentelemetry-exporter-otlp-proto-grpc`.  
`setup_telemetry()` is called at startup in `main.py` before anything else.  
The `Consumer._process_job` method wraps each compute job in a `route_calc.compute` span and increments `route_calc.jobs.processed` / `route_calc.jobs.failed` counters.

## Adding custom spans / metrics

**Python:**
```python
from opentelemetry import trace, metrics
tracer = trace.get_tracer(__name__)
with tracer.start_as_current_span("my.operation"):
    ...
```

**.NET:**
```csharp
using var activity = MyActivitySource.StartActivity("my.operation");
```

## Teardown

```bash
helm uninstall otel-collector loki tempo kube-prometheus-stack -n monitoring
kubectl delete namespace monitoring
```
