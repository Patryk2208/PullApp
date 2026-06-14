# Payments — Components (C4 Level 3)

**Status: planned / not implemented.** Trip Planner integrates against the
`IPaymentsService` interface, backed by a `FakePaymentsService` for local dev.

## Intended responsibilities

- Quote + **freeze** the ride price when a passenger creates a ride request.
- **Unfreeze** on rejection / cancellation before pickup.
- **Charge** the price on ride completion, or the `cancellation_price` on late
  cancellation / no-show.
- Wallet + ledger ownership; settlement to drivers.

## Integration today

Trip Planner depends only on the interface, so the real service can drop in without
domain changes. The points where money moves are documented in the
[ride lifecycle flow](../04-flows/ride-lifecycle.md#payments--funds): freeze on
request (flow 3), unfreeze on reject (flow 4), charge/penalty on cancel/complete
(flow 8).

When implemented: .NET service, owns a PostgreSQL ledger, talks to an external
payment gateway (Stripe / Przelewy24) over HTTPS.
