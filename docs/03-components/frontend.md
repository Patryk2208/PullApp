# Frontend — Components (C4 Level 3)

The web client. Single Next.js app in a turborepo, talking only to the
[gateway](gateway.md) over REST + SSE.

Source: `src/frontend/pullapp-frontend/`

## Layout (turborepo + pnpm)

```
apps/web/                 Next.js App Router app (the deployable)
packages/
  @pullapp/domain         domain types + Result<T> (shared contracts)
  @pullapp/api-client     gateway client + repositories (UserRepository, AuthRepository)
  @pullapp/features       hooks + zustand stores (auth, trips, notifications)
  @pullapp/ui             shared components
  typescript-config       shared tsconfig
```

The dependency direction is `apps/web → features → api-client → domain`. UI state
and data-fetching logic live in `features`; pages stay thin.

## Routes (`apps/web/app`)

| Route | Purpose |
|-------|---------|
| `/login`, `/register` | auth |
| `/profile` | current user (`/api/users/me`) |
| `/trips/search` | passenger: search routes, send a ride request |
| `/trips/my-rides` | passenger: my requests + rides, cancel / pickup / end |
| `/trips/publish` | driver: publish + activate a route |
| `/trips/driver` | driver: incoming requests (accept/reject), active rides, pickup/end |

## Features (`packages/features/src`)

| Module | What it does |
|--------|--------------|
| `auth/authStore.ts` | zustand (+persist) — JWT + user; rehydrated from localStorage |
| `auth/useLogin` · `useRegister` · `useProfile` | auth flows + `/me` |
| `trips/usePublishTrip` · `useSearchTrips` | driver publish, passenger search |
| `trips/useRideActions` | accept / reject / cancel / pickup / end calls |
| `trips/useMyTrips` · `ridesStore.ts` | read-model fetch + client cache of my requests/rides |
| `notifications/useNotificationStream` | **singleton shared SSE** connection (see below) |

## State

- **Auth** — zustand store persisted to localStorage; the JWT is attached as a
  `Bearer` token on every gateway call by the api-client.
- **Trips** — `ridesStore` caches the read-model responses so the my-rides and
  driver views don't refetch on every interaction.

## Realtime (SSE)

`useNotificationStream` opens **one** `EventSource` to `/sse/notifications`
(gateway → notifications `/stream`) and shares it across all components via a
module-level singleton — multiple mounted components do not each open a connection.
A top-level `NotificationListener` component subscribes once and dispatches events
into the trips store. See [notifications-sse flow](../04-flows/notifications-sse.md).

## Gateway contract

All calls go through the gateway prefix `/api/route/**` (stripped to the bare
trip-planner path), `/api/auth/**`, `/api/users/**`, `/sse/notifications`. The
endpoints consumed today:

```
POST /api/auth/register · /api/auth/login
GET  /api/users/me
POST /api/route/driver/routes                       · /{id}/activate · DELETE /{id}
GET  /api/route/driver/requests · /driver/rides
POST /api/route/driver/requests/{id}/accept · /reject
POST /api/route/driver/rides/{id}/pickup · /end
POST /api/route/passenger/routes/search
POST /api/route/passenger/routes/{id}/requests
GET  /api/route/passenger/requests · /passenger/rides
POST /api/route/passenger/rides/{id}/pickup · /end · DELETE /{id}
```

These map 1:1 to [trip-planner endpoints](trip-planner.md#http-endpoints) (the
`/api/route` prefix is removed by YARP).

## Local dev notes

- Dev runs on a configurable port (e.g. `next dev -p 5000`).
- **E2E (Playwright) must run against a production build** (`next build && next start`):
  `next dev`'s HMR WebSocket does not complete the handshake in headless Chrome, so
  the app never hydrates and tests see a blank page. Tests use `playwright-core`
  with the system Chrome (`channel: 'chrome'`, headless).
