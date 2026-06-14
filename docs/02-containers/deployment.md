# Deployment

How the containers from [containers.md](containers.md) map onto the local runtime.
Production topology (CDN, managed DBs, real payment/push providers) is out of scope
here — this describes the **minikube + docker-compose** local stack that everything
is actually run against.

## Topology

```
┌─ host machine ──────────────────────────────────────────────┐
│                                                              │
│  docker-compose (src/infrastructure/compose/)                │
│    Postgres · PostGIS · Redis · RabbitMQ · Kafka             │
│         ▲                                                    │
│         │  host.minikube.internal                            │
│  ┌──────┴───────────── minikube (docker driver) ──────────┐  │
│  │                                                         │  │
│  │  namespace: pullapp                                     │  │
│  │    gateway · accounts · trip-planner · route-calc       │  │
│  │    notifications · driver-tracker                       │  │
│  │    + ExternalName Services → host.minikube.internal     │  │
│  │                                                         │  │
│  │  namespace: monitoring                                  │  │
│  │    kube-prometheus-stack (Prometheus + Grafana)         │  │
│  │    Loki · Tempo · otel-collector                        │  │
│  │                                                         │  │
│  │  KEDA → scales route-calc on RabbitMQ queue depth       │  │
│  └─────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
```

## Key decisions

- **Stateful deps live outside the cluster.** Postgres/PostGIS, Redis, RabbitMQ and
  Kafka run in **docker-compose**, not in-cluster. Pods reach them through
  Kubernetes **`ExternalName`** services that resolve to `host.minikube.internal`.
  This keeps the cluster cattle-only and lets you restart the cluster without
  losing data or waiting for stateful sets.
- **`imagePullPolicy: Never`.** Images are built locally and loaded into minikube's
  Docker daemon; pods never pull from a registry.
- **Kustomize** with `base/` + `overlay/local/` under
  `src/infrastructure/k8s/`. The `pullapp` namespace is always used. Secrets use
  `secretGenerator` with `PLACEHOLDER` values that must be replaced for real deploys.
- **KEDA** drives route-calc autoscaling (scale-to-zero) on RabbitMQ queue depth —
  see `src/infrastructure/k8s/base/services/route-calc/scaled-object.yaml`. A side
  effect: route-calc-only metrics (`route_calc_*`) are **ephemeral** — they vanish
  when the deployment scales to zero between jobs.

## Build & deploy

All orchestration is in the **top-level `Makefile`** (`make help` lists targets):

```bash
make start      # first-time: minikube + observability stack + KEDA
make infra      # docker-compose deps (db, cache, messaging)
make run        # full from scratch: cluster + obs + infra + build/load + deploy
make ci         # build all images, load into minikube, restart deployments
make ci-<svc>   # one service (e.g. make ci-accounts)
make cd         # kubectl apply kustomize overlay + wait for rollouts
make pf-gateway # port-forward gateway → http://localhost:8080
make status     # cluster + pod + compose summary
```

### Reliable image reload (gotcha)

`minikube image load` does **not** overwrite an existing `:latest` tag, and with
`imagePullPolicy: Never` pods keep running the stale image. To force a real update:

```bash
docker save <image>:latest | (eval $(minikube docker-env); docker load)
kubectl rollout restart deployment/<svc> -n pullapp
```

### trip-planner build context

The generic `make ci-<svc>` target uses `src/services/<svc>` as the Docker build
context. **trip-planner is the exception** — its Dockerfile needs the cross-service
`schemas/`, so it must be built with `src/` as the context.

## Observability install

The `monitoring` namespace is installed separately (Helm) — see
[observability overview](../05-observability/observability.md). Grafana
auto-discovers dashboards from any ConfigMap labelled `grafana_dashboard: "1"`.
