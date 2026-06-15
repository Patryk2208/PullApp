#!/usr/bin/env bash
# Live view of KEDA scaling route-calc while stress-routecalc.sh runs.
# Shows: route-calc pods, the KEDA-managed HPA (current vs target queue depth),
# and the raw RabbitMQ compute-queue depth.
set -uo pipefail
NS=pullapp
INTERVAL="${INTERVAL:-2}"

while true; do
  clear
  printf '\033[1m=== route-calc pods (%s) ===\033[0m\n' "$(date +%T)"
  kubectl get pods -n "$NS" -l app=route-calc -o wide 2>/dev/null
  printf '\n\033[1m=== KEDA HPA (current/target avg queue depth) ===\033[0m\n'
  kubectl get hpa keda-hpa-route-calc-scaler -n "$NS" 2>/dev/null
  printf '\n\033[1m=== compute-queue depth (RabbitMQ) ===\033[0m\n'
  kubectl exec -n "$NS" deploy/trip-planner -c trip-planner -- true 2>/dev/null # noop keep-alive
  kubectl exec -n "$NS" "$(kubectl get pod -n "$NS" -l app=compute-queue -o name 2>/dev/null | head -1)" -- \
      rabbitmqctl list_queues name messages 2>/dev/null | grep -E 'compute|results' \
    || printf '  (rabbitmq pod not in this ns — use the management UI: make pf-rabbit → http://localhost:15672)\n'
  sleep "$INTERVAL"
done
