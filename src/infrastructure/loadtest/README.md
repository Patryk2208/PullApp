# Load testing — watch KEDA scale route-calc

Floods the RabbitMQ `compute-queue` with matching / route-geometry jobs so you can
watch the KEDA ScaledObject scale `route-calc` up under load.

## How it works

Every passenger **search** (`POST /api/route/passenger/routes/search`) and driver
**route create** (`POST /api/route/driver/routes`) makes trip-planner publish a
compute job to `compute-queue`. The ScaledObject targets **10 queued msgs per
replica** (`min 1`, `max 20`), polls every 10s. Submit faster than one replica can
drain → the queue backs up → KEDA scales out.

## Run it

```bash
# 1. expose the gateway (one shell, keep it open)
make pf-gateway                       # → http://localhost:8080

# 2. watch the scaling (another shell)
src/infrastructure/loadtest/watch-keda.sh

# 3. generate load (a third shell)
src/infrastructure/loadtest/stress-routecalc.sh
#   knobs:  DURATION=240  CONCURRENCY=40  MODE=mix|search|create
```

The stress script registers/logs in a throwaway user, grabs a JWT, then fires
sustained concurrent bursts and prints a per-wave line (submitted / ok / err /
current replica count).

## What you'll see

Under default load on a 16-CPU minikube node, route-calc climbs roughly:

```
t+0s   1 replica
t+17s  5
t+34s  10
t+50s  20 (desired)   ← KEDA hits maxReplicaCount
```

## Heads-up: cluster capacity caps the demo

route-calc requests **1 CPU + 2 GiB per replica**. A single minikube node (16 CPU)
fills up around **~12 replicas** — the rest sit `Pending` with `Insufficient cpu`.
That's expected and is itself the lesson: **KEDA's scaling decision is independent
of whether the cluster can actually schedule the pods.** To watch a clean scale to
the full 20, do one of:

- give minikube more CPU: `minikube config set cpus 24 && minikube delete && make start`
- or lower route-calc's CPU request (e.g. `250m`) in
  `src/infrastructure/k8s/base/services/route-calc/`
- or lower `maxReplicaCount` to ~10 in the ScaledObject so it fits.

After the load stops, KEDA scales back to 1 (60s cooldown + 120s scale-down
stabilization).
