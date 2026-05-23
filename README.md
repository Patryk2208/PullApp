# PullApp

A ride-sharing platform built as a monorepo of microservices.

Documentation is in [`./docs`](./docs):
- [`01-containers`](./docs/01-containers) — system architecture and container diagram
- [`02-bounded-contexts`](./docs/02-bounded-contexts) — context division and use-cases
- [`03-flows`](./docs/03-flows) — main system flows
- [`04-components`](./docs/04-components) — detailed service descriptions

---

## Prerequisites

| Tool | Purpose | Install |
|------|---------|---------|
| `docker` + Docker Desktop / daemon | Build images, run local deps | https://docs.docker.com/get-docker |
| `minikube` | Local Kubernetes cluster | https://minikube.sigs.k8s.io/docs/start |
| `kubectl` | Cluster management | https://kubernetes.io/docs/tasks/tools |
| `kustomize` | K8s overlay rendering | https://kubectl.docs.kubernetes.io/installation/kustomize |
| `helm` | Observability stack install | https://helm.sh/docs/intro/install |

Verify everything is in place:
```bash
docker info && minikube version && kubectl version --client && kustomize version && helm version
```

---

## Quick start

### First time on a fresh machine

```bash
# 1. Start minikube and install the observability stack (Prometheus, Grafana, Loki, Tempo, OTel)
make start

# 2. Copy and fill in secrets
cp src/infrastructure/compose/env.example src/infrastructure/compose/.env
# edit the file — see "Environment variables" below

# 3. Build all service images, load into minikube, deploy
make run
```

`make run` is idempotent — safe to re-run after code changes.

### Day-to-day

```bash
make ci          # rebuild all images + load into minikube
make cd          # deploy (kubectl apply + rollout wait)

make ci-gateway  # rebuild a single service
make restart     # rolling restart all deployments (no rebuild)

make status      # cluster + pod + compose summary
```

### Access the running stack

```bash
make pf-gateway     # app API       → http://localhost:8080
make pf-grafana     # Grafana       → http://localhost:3000  (admin / pullapp-grafana)
make pf-prometheus  # Prometheus    → http://localhost:9090
make pf-loki        # Loki          → http://localhost:3100
make pf-rabbit      # RabbitMQ UI   → http://localhost:15672
```

Run `make help` for the full target list.

---

## Local infrastructure (compose deps only)

If you only want the backing services (databases, cache, messaging) without Kubernetes:

```bash
make infra          # start everything (db + cache + messaging)
make infra-db       # databases only
make infra-cache    # Redis only
make infra-messaging # RabbitMQ + Kafka only

make infra-down     # stop and remove containers
make infra-clean    # stop + delete volumes (destructive)
```

---

## Environment variables

All compose secrets live in `src/infrastructure/compose/.env`. Copy the example and update before first use:

```bash
cp src/infrastructure/compose/.env.example src/infrastructure/compose/.env
```

| Variable | Service | Default (example) |
|----------|---------|-------------------|
| `TRIP_PLANNER_POSTGRES_DB` | trip-planner DB | `trip-planner` |
| `TRIP_PLANNER_POSTGRES_USER` | trip-planner DB | `pullapp` |
| `TRIP_PLANNER_POSTGRES_PASSWORD` | trip-planner DB | — |
| `ACCOUNTS_POSTGRES_DB` | accounts DB | `accounts` |
| `ACCOUNTS_POSTGRES_USER` | accounts DB | `pullapp` |
| `ACCOUNTS_POSTGRES_PASSWORD` | accounts DB | — |
| `NOTIFICATIONS_POSTGRES_DB` | notifications DB | `notifications` |
| `NOTIFICATIONS_POSTGRES_USER` | notifications DB | `pullapp` |
| `NOTIFICATIONS_POSTGRES_PASSWORD` | notifications DB | — |
| `TRIP_REDIS_PASSWORD` | trip/route-calc cache | — |
| `DRIVER_TRACKER_REDIS_PASSWORD` | driver-tracker cache | — |
| `RABBITMQ_USER` | compute queue | `pullapp` |
| `RABBITMQ_PASS` | compute queue | — |
| `CHAT_MONGO_USER` | chat DB | `pullapp` |
| `CHAT_MONGO_PASSWORD` | chat DB | — |
| `CHAT_MONGO_DB` | chat DB | `chat` |

Kubernetes secrets are managed separately via Kustomize `secretGenerator` in `src/infrastructure/k8s/overlay/local/secrets/secrets.env`.

---

## Observability

The stack is installed by `make obs-install` (called automatically by `make start`):

| Component | Access | Credentials |
|-----------|--------|-------------|
| Grafana | `make pf-grafana` → :3000 | admin / pullapp-grafana |
| Prometheus | `make pf-prometheus` → :9090 | — |
| Loki | `make pf-loki` → :3100 | — |
| Tempo | `make pf-tempo` → :4317 (OTLP gRPC) | — |

Dashboards are loaded automatically via ConfigMaps with the `grafana_dashboard: "1"` label.

To upgrade Helm chart versions: `make obs-upgrade`

---

## After infrastructure changes (k8s manifest edits)

```bash
make reset   # delete all pullapp resources + re-apply from scratch
```

Use this when you change Deployments, Services, ConfigMaps, or Secrets — not for code-only changes (use `make ci && make cd` for those).
