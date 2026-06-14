# Gateway — Components (C4 Level 3)

The single ingress. A thin .NET 10 app built around **YARP** (Yet Another Reverse
Proxy). It terminates client traffic, validates the JWT, injects identity headers,
and forwards HTTP to the internal services.

Source: `src/services/gateway/`

## Responsibilities

1. **Reverse proxy** — route client requests to the right backend cluster (YARP).
2. **AuthN** — validate the JWT issued by [accounts](accounts.md).
3. **Identity propagation** — inject `X-User-Id` / `X-User-Role` downstream so
   internal services don't re-parse the token.
4. **Metrics** — record per-service request count + latency (see below).

## Routing table (YARP, `appsettings.json`)

| Route | Path | Cluster | Auth | Transform |
|-------|------|---------|------|-----------|
| accounts-auth | `/api/auth/{**}` | accounts | **Anonymous** | — |
| accounts-users | `/api/users/{**}` | accounts | default | — |
| trip-planner | `/api/route/{**}` | trip-planner | default | strip `/api/route` prefix |
| notifications | `/sse/notifications` | notifications | default | rewrite to `/stream` |
| driver-tracker-position | `/api/tracker/position/{routeId}` | driver-tracker | default | → `/position/{routeId}` |
| driver-tracker-track | `/ws/driver-tracker/{routeId}` | driver-tracker | default | → `/track/{routeId}` |

Clusters resolve to in-cluster service DNS (`http://<svc>:80`). Only the auth route
is anonymous; everything else requires a valid token (`default` policy).

## JWT validation

The gateway validates tokens against the same symmetric key accounts signs with.
The signing key's **`KeyId` must be `pullapp-key`** to match the `kid` header the
issuer sets — otherwise the key resolver fails and every request 401s. Inbound
claim mapping is disabled (`MapInboundClaims = false`) so `sub` / `role` stay
verbatim. After validation, middleware copies the identity into `X-User-Id` /
`X-User-Role` headers for downstream services (trip-planner trusts `X-User-Id`).

> **Trust boundary:** internal services trust `X-User-*` headers because they are
> only reachable through the gateway inside the cluster. Hardening that to verify
> the JWT `sub` end-to-end at each service is a known, deliberately deferred item.

## Metrics (`GatewayMetrics.cs`, meter `Gateway`)

| Instrument | Type | Labels |
|------------|------|--------|
| `gateway_requests_total` | counter | `service`, `method`, `status_code` |
| `gateway_request_duration_seconds` | histogram | `service`, `method` |

Recorded in middleware on every proxied response. The meter **pre-seeds zero-value
series** for all `{service × method × status}` combinations at startup, so Grafana
panels render axes and legends even before any traffic flows (no "No data").
Also emits standard ASP.NET Core instrumentation (`http_server_*`,
`aspnetcore_diagnostics_exceptions_total`).
