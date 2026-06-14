# Accounts — Components (C4 Level 3)

User identity and authentication. The **JWT issuer** for the whole platform.

Source: `src/services/accounts/`

## Architecture

Clean Architecture: `Domain → Application → Infrastructure → Api`.

- **Domain** — `User` aggregate (credentials, profile fields), value objects.
- **Application** — one MediatR handler per use case (`RegisterUserHandler`,
  `LoginUserHandler`), repository + service interfaces, FluentValidation.
- **Infrastructure** — EF Core `AccountsDbContext` (PostgreSQL), `JwtProvider`,
  password hasher.
- **Api** — minimal-API endpoints (`IEndpoint`), JWT bearer setup,
  `GlobalExceptionHandler`, health checks, OpenTelemetry.

## HTTP endpoints

| Method | Path (via gateway) | Auth | Handler |
|--------|--------------------|------|---------|
| POST | `/api/auth/register` | anonymous | `RegisterUserHandler` |
| POST | `/api/auth/login` | anonymous | `LoginUserHandler` → `{ accessToken }` |
| GET | `/api/users/me` | **required** | returns the caller's profile |

> Login returns `{ "accessToken": "<jwt>" }`. The field is `accessToken` — the
> frontend reads exactly that (a past bug was a `token` vs `accessToken` mismatch
> across the two sides).

## JWT issuance

`JwtProvider.Generate(user)` mints an HS256 token signed with `Jwt:SecretKey` and
sets the **`kid` header to `pullapp-key`**. The [gateway](gateway.md#jwt-validation)
validates against the same key + `kid`; a mismatch 401s everything. Claims
(`sub`, `email`, `role`) are emitted verbatim — both issuer and gateway disable
inbound claim-type mapping (`MapInboundClaims = false`).

Accounts itself also wires `UseAuthentication()` / `UseAuthorization()` so that
`/me` (which `RequireAuthorization()`) works when called directly, not only through
the gateway.

## Persistence

EF Core over PostgreSQL. On startup the app runs `db.Database.Migrate()`, wrapped to
**swallow `42P07` (relation already exists)** — a dev database created by a previous
run without migration history would otherwise crash the service on every restart.

## Metrics (`AccountsMetrics.cs`, meter `Accounts`)

| Instrument | Type | Prometheus name |
|------------|------|-----------------|
| `accounts.registrations` | counter | `accounts_registrations_users_total` |
| `accounts.login.success` | counter | `accounts_login_success_attempts_total` |
| `accounts.login.failed` | counter | `accounts_login_failed_attempts_total` |
| `accounts.validation.failures` | counter | `accounts_validation_failures_total` |
| `accounts.login_duration_seconds` | histogram | `accounts_login_duration_seconds_*` |

(See [metrics.md](../05-observability/metrics.md) for the OTel→Prometheus naming
rules — dots become underscores, the unit is appended, counters get `_total`.)

## Security scope (deliberate)

`/me` and the auth flow are intentionally confined to **accounts + gateway** — no
other service is involved. The gateway is the only path in, so internal services
trust `X-User-*`; verifying the JWT `sub` per-service is a known deferred item, not
a gap in this slice.
