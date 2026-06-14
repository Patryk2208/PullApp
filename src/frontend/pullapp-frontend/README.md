# PullApp Frontend

- `apps/native` – a [react-native](https://reactnative.dev/) app built with [expo](https://docs.expo.dev/)
- `apps/web` – a [Next.js](https://nextjs.org/) app built with [react-native-web](https://necolas.github.io/react-native-web/)


- `packages/domain` – model classes, interfaces
- `packages/app-client` – concrete implementations that call APIs
- `packages/features` – business logic, use cases
- `packages/ui` – a [react-native](https://reactnative.dev/) component library shared by both `web` and `native`


- `packages/typescript-config` – `tsconfig.json`s used throughout the monorepo
- [TypeScript](https://www.typescriptlang.org/) – static type checking
- [Prettier](https://prettier.io) – code formatting

Run:
```bash
pnpm install
pnpm run dev # --filter @pullapp/web
```

## Frontend – Implemented flows

### Implemented
- **Flow 0** – driver publishes a route (`POST /driver/routes`) + activation (`POST /driver/routes/{id}/activate`)
- **Flow 2** – passenger searches for matching rides (`POST /passenger/routes/search`) – results delivered via SSE
- **Flow 3** – passenger sends a join request (`POST /passenger/routes/{routeId}/requests`)
- **Flow 4** – driver rejects a request (`POST /driver/requests/{id}/reject`)
- **Flow 5** – driver accepts a request (`POST /driver/requests/{id}/accept`)

### TODO
- **Flow 1.5** – driver deletes a route
- **Flow 7** – mutual pickup confirmation (both sides)
- **Flow 8** – ride cancellation / completion

## Assumptions and limitations

**SSE active only on dedicated subpages** – `/trips/driver` listens for `ride_requested`, `/trips/search` opens SSE only for the duration of a search. The driver must be on the dashboard before the passenger sends a request, otherwise the notification is lost.

**SSE proxied via Next.js route handler** (`/app/api/sse/route.ts`) – direct connection to the Gateway was blocked by CORS; a server-side proxy resolves this.

**No driver route geometry** – `MatchedRoute` from SSE contains only pickup/dropoff point indices, not coordinates. The map modal shows the passenger's selected points, not the driver's route.

**Payments** – no payments service exists yet; `FakePaymentsService` in trip-planner handles Flow 3 and Flow 8 with a fixed quote of 25.50 PLN and no-op charge/unfreeze calls.