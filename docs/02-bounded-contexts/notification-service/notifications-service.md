# Notifications Service — Bounded Context

## Overview

Reacts to domain events from Kafka and delivers push notifications to the relevant user's device via FCM. No SMS, no email — push only. Stateless, horizontally scalable. Owns device token registration and delivery idempotency.

---

## Actors

| Actor | Interaction |
|-------|-------------|
| Mobile app (driver/passenger) | `POST /devices/register` — registers or refreshes FCM token on startup |
| Kafka | Source of domain events — Trip Planner, Payments emit events consumed here |
| FCM (Firebase Cloud Messaging) | Delivery target — Go Firebase Admin SDK |

---

## Flow 1: Device token registration

Mobile app calls on every startup (not just first install — tokens rotate):

```
POST /devices/register
{ "userId": "...", "token": "dOFzMx3kS0:APA91b...", "platform": "android" }
```

Notifications upserts to Postgres:

```sql
INSERT INTO device_tokens (user_id, token, platform, updated_at)
VALUES ($1, $2, $3, now())
ON CONFLICT (user_id) DO UPDATE
    SET token = EXCLUDED.token,
        platform = EXCLUDED.platform,
        updated_at = now();
```

Returns 204. No auth beyond JWT validation at the gateway.

---

## Flow 2: Kafka event → push notification

```
Kafka event in
  → check sent_notifications for event_id (idempotency)
  → if already processed: ack and skip
  → switch on event type → resolve recipient userId + build payload
  → SELECT token FROM device_tokens WHERE user_id = $1
  → if no token: ack and skip (user never registered a device)
  → client.Send(ctx, &messaging.Message{...})
  → if FCM error IsRegistrationTokenNotRegistered: DELETE token row
  → INSERT INTO sent_notifications (event_id, sent_at)
  → ack Kafka message
```

---

## Event mapping

| Kafka event | Recipient | FCM priority | Title | Body |
|-------------|-----------|--------------|-------|------|
| `ride.accepted` | passenger | high | Driver accepted | "{driverName} is on the way" |
| `ride.cancelled.by_driver` | passenger | high | Ride cancelled | "Your driver cancelled the ride" |
| `ride.cancelled.by_passenger` | driver | high | Ride cancelled | "Passenger cancelled the ride" |
| `driver.arriving` | passenger | high | Driver arriving | "{driverName} is almost there" |
| `ride.completed` | passenger | normal | Ride complete | "Your ride has ended" |
| `payment.charged` | passenger | normal | Payment confirmed | "Payment of {amount} confirmed" |

Templates and priorities are hardcoded in a switch — no database-backed template system.

---

## FCM send

Uses the official Firebase Admin SDK for Go: `firebase.google.com/go/v4/messaging`

```go
_, err := client.Send(ctx, &messaging.Message{
    Token: token,
    Notification: &messaging.Notification{
        Title: title,
        Body:  body,
    },
    Android: &messaging.AndroidConfig{
        Priority: "high", // or "normal"
    },
    APNS: &messaging.APNSConfig{
        Headers: map[string]string{
            "apns-priority": "10", // 10 = high, 5 = normal
        },
    },
})

if messaging.IsRegistrationTokenNotRegistered(err) {
    // stale token — delete it
    db.Exec("DELETE FROM device_tokens WHERE user_id = $1", userID)
}
```

Firebase Admin SDK is initialized once at startup with a service account JSON file mounted as a K8s secret. SDK handles OAuth token refresh internally.

---

## Postgres schema

```sql
-- Device tokens: one row per user, upserted on every app startup
CREATE TABLE device_tokens (
    user_id    UUID PRIMARY KEY REFERENCES users(id),
    token      TEXT NOT NULL,
    platform   VARCHAR(10) NOT NULL, -- 'ios' or 'android'
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Idempotency log: prevents double-sends on Kafka redelivery
CREATE TABLE sent_notifications (
    event_id  TEXT PRIMARY KEY,
    sent_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Optional: clean up old idempotency rows after 7 days
-- (event redelivery window is far shorter than 7 days)
CREATE INDEX ON sent_notifications (sent_at);
```

---

## Scaling

Stateless — any pod can handle any Kafka event. Scale by adding consumers up to the partition count of the Kafka topic. No sticky sessions, no shared in-memory state.

---

## What this service does NOT do

- No SMS or email delivery
- No per-user notification preferences (future enhancement)
- No in-app notification inbox
- Does not know about ride state beyond what arrives in Kafka events
- Does not call Trip Planner, Accounts, or any other service — all needed data must be present in the Kafka event payload
