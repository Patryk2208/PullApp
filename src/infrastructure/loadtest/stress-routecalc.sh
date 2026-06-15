#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────────────────────
# Route-calc stress test — floods the RabbitMQ `compute-queue` with matching /
# route-geometry jobs so you can watch KEDA scale route-calc up.
#
# Every passenger search and driver route-create the gateway forwards makes
# trip-planner publish a compute job to `compute-queue`. The KEDA ScaledObject
# targets 10 queued msgs per replica (min 1, max 20), so ~100 backed-up msgs →
# ~10 replicas. We submit faster than one replica can drain → the queue builds →
# KEDA scales. Sustained for the whole DURATION so replicas stay up.
#
# Usage:
#   ./stress-routecalc.sh                 # 4 min, 40 concurrent, mixed
#   DURATION=120 CONCURRENCY=80 ./stress-routecalc.sh
#   MODE=search ./stress-routecalc.sh     # search | create | mix (default mix)
#
# Prereq: gateway reachable at $GATEWAY (default http://localhost:8080).
#   In another shell:  make pf-gateway
# Watch it scale:      ./watch-keda.sh   (or see commands it prints)
# ──────────────────────────────────────────────────────────────────────────────
set -uo pipefail

GATEWAY="${GATEWAY:-http://localhost:8080}"
DURATION="${DURATION:-240}"        # seconds of sustained load
CONCURRENCY="${CONCURRENCY:-40}"   # parallel requests per wave
MODE="${MODE:-mix}"                # search | create | mix
EMAIL="${EMAIL:-loadtest@pullapp.dev}"
PASSWORD="${PASSWORD:-loadtest123}"

C_CYAN=$'\033[0;36m'; C_GRN=$'\033[0;32m'; C_YEL=$'\033[1;33m'; C_RED=$'\033[0;31m'; C_RST=$'\033[0m'
say() { printf "%s\n" "$*"; }

# ── 0. sanity ──────────────────────────────────────────────────────────────────
if ! curl -fsS -o /dev/null "$GATEWAY/api/auth/login" -X POST -H 'Content-Type: application/json' -d '{}' 2>/dev/null; then
  : # a 400/401 is fine — gateway is up; only a connection refusal matters
fi
if ! curl -sS -o /dev/null -w '' "$GATEWAY" 2>/dev/null; then
  say "${C_RED}Gateway not reachable at $GATEWAY — run 'make pf-gateway' first.${C_RST}"; exit 1
fi

# ── 1. get a JWT (register, ignore-if-exists, then login) ───────────────────────
say "${C_CYAN}Getting a token for $EMAIL ...${C_RST}"
curl -sS -o /dev/null "$GATEWAY/api/auth/register" \
  -H 'Content-Type: application/json' \
  -d "{\"Name\":\"Load\",\"Surname\":\"Tester\",\"Email\":\"$EMAIL\",\"Password\":\"$PASSWORD\",\"BirthDate\":\"1995-01-01\"}" || true

LOGIN_JSON=$(curl -sS "$GATEWAY/api/auth/login" \
  -H 'Content-Type: application/json' \
  -d "{\"Email\":\"$EMAIL\",\"Password\":\"$PASSWORD\"}")
if command -v jq >/dev/null 2>&1; then
  TOKEN=$(printf '%s' "$LOGIN_JSON" | jq -r '.accessToken // empty')
else
  TOKEN=$(printf '%s' "$LOGIN_JSON" | grep -oE '"accessToken"[^"]*"[^"]+"' | sed -E 's/.*"accessToken"[^"]*"([^"]+)".*/\1/')
fi
if [ -z "${TOKEN:-}" ]; then
  say "${C_RED}Login failed — no accessToken.${C_RST} Response: $LOGIN_JSON"; exit 1
fi
say "${C_GRN}Token acquired.${C_RST}"

AUTH="Authorization: Bearer $TOKEN"
TMP=$(mktemp -d); trap 'rm -rf "$TMP"' EXIT

# ── helpers ─────────────────────────────────────────────────────────────────────
# random Warsaw-area coordinate (inside service area)
coord() { awk -v s="$RANDOM$1" 'BEGIN{srand(s); printf "%.5f,%.5f", 52.10+rand()*0.25, 20.90+rand()*0.30}'; }

submit_search() {
  local a b; a=$(coord A); b=$(coord B)
  local slat=${a%,*} slng=${a#*,} elat=${b%,*} elng=${b#*,}
  local dep=$(( ($(date +%s) + 3600) * 1000 ))
  curl -s -o /dev/null -w '%{http_code}\n' "$GATEWAY/api/route/passenger/routes/search" \
    -H 'Content-Type: application/json' -H "$AUTH" \
    -d "{\"Start\":{\"Latitude\":$slat,\"Longitude\":$slng},\"End\":{\"Latitude\":$elat,\"Longitude\":$elng},\"DepartureDate\":$dep,\"SeatsNeeded\":1,\"MaxDetourKm\":10,\"TimeWindowMinutes\":45}"
}

submit_create() {
  local a b; a=$(coord A); b=$(coord B)
  local slat=${a%,*} slng=${a#*,} elat=${b%,*} elng=${b#*,}
  curl -s -o /dev/null -w '%{http_code}\n' "$GATEWAY/api/route/driver/routes" \
    -H 'Content-Type: application/json' -H "$AUTH" \
    -d "{\"Start\":{\"Latitude\":$slat,\"Longitude\":$slng},\"End\":{\"Latitude\":$elat,\"Longitude\":$elng},\"Capacity\":3}"
}

submit_one() {
  case "$MODE" in
    search) submit_search ;;
    create) submit_create ;;
    *)      if (( RANDOM % 2 )); then submit_search; else submit_create; fi ;;
  esac
}
export -f submit_one submit_search submit_create coord
export GATEWAY AUTH MODE

# ── 2. sustained load ────────────────────────────────────────────────────────────
say ""
say "${C_YEL}Stressing route-calc:${C_RST} mode=$MODE  concurrency=$CONCURRENCY  duration=${DURATION}s  →  $GATEWAY"
say "${C_CYAN}Watch it scale (another shell):${C_RST}"
say "  watch -n2 'kubectl get pods -n pullapp -l app=route-calc; echo; kubectl get hpa -n pullapp'"
say ""

START=$(date +%s); DEADLINE=$((START + DURATION)); TOTAL=0
while [ "$(date +%s)" -lt "$DEADLINE" ]; do
  : > "$TMP/wave"
  for _ in $(seq 1 "$CONCURRENCY"); do submit_one >> "$TMP/wave" & done
  wait
  n=$(wc -l < "$TMP/wave"); TOTAL=$((TOTAL + n))
  ok=$(grep -cE '^(200|201|202|204)$' "$TMP/wave" || true)
  bad=$((n - ok))
  reps=$(kubectl get deploy route-calc -n pullapp -o jsonpath='{.status.readyReplicas}' 2>/dev/null || echo '?')
  qd=$(kubectl get hpa keda-hpa-route-calc-scaler -n pullapp -o jsonpath='{.status.currentMetrics[0].external.current.averageValue}' 2>/dev/null || echo '?')
  printf "%s[t+%03ds]%s submitted=%-5d ok=%-4d err=%-3d | route-calc replicas=%s\n" \
    "$C_CYAN" "$(( $(date +%s) - START ))" "$C_RST" "$TOTAL" "$ok" "$bad" "${reps:-0}"
done

say ""
say "${C_GRN}Done.${C_RST} Total submitted: $TOTAL. KEDA cooldown is 60s + 120s scale-down stabilization, so replicas linger then drop."
say "Final state:"
kubectl get pods -n pullapp -l app=route-calc 2>/dev/null || true
kubectl get hpa keda-hpa-route-calc-scaler -n pullapp 2>/dev/null || true
