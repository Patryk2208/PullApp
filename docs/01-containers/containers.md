# PullApp - Container Architecture (Level 2)

## Service breakdown

### 1. Trip Planner

| Attribute | Description |
|-----------|-------------|
| **Purpose** | Ride orchestration, state management, driver route registration, passenger request handling |
| **Technology** | .NET 10 |
| **Communication** | HTTP/WebSocket (clients), gRPC (services), AMQP (RabbitMQ) |
| **Scaling** | Horizontal, stateless |
| **Data** | Writes to PostGIS, reads/writes Redis |
| **Bounded-Context** | [../02-bounded-contexts/trip-planner] |

---

### 2. Route-Calc

| Attribute | Description |
|-----------|-------------|
| **Purpose** | Compute distance matrices, score drivers, find optimal matches |
| **Technology** | C++20 with linked OSRM library |
| **Communication** | AMQP (RabbitMQ consumer), gRPC (results), HTTP (OSRM internal) |
| **Scaling** | KEDA auto-scaling based on queue depth |
| **Data** | Reads PostGIS (candidate drivers), reads OSM data (in-memory), writes Redis cache |
| **Bounded-Context** | [../02-bounded-contexts/route-calc] |

**Resource Requirements !!!important, dependant on the region size(Poland here):**
- 8GB RAM per instance (OSRM routing graph)
- 4 CPU cores per instance

---

### 3. Account Service

| Attribute | Description |
|-----------|-------------|
| **Purpose** | User identity, authentication, profile management, verification |
| **Technology** | .NET 10 |
| **Communication** | gRPC (internal), HTTP (admin) |
| **Scaling** | Horizontal, stateless |
| **Data** | Owns PostgreSQL database |
| **Bounded-Context** | [../02-bounded-contexts/account-service] |

---

### 4. Payment Service

| Attribute | Description |
|-----------|-------------|
| **Purpose** | Financial transaction processing, wallet management, settlement |
| **Technology** | .NET 10 |
| **Communication** | gRPC (internal), HTTP (payment gateways) |
| **Scaling** | Horizontal, stateless |
| **Data** | Owns PostgreSQL database |
| **Bounded-Context** | [../02-bounded-contexts/payment-service] |

---

### 5. Chat Service

| Attribute | Description |
|-----------|-------------|
| **Purpose** | Real-time messaging between drivers and passengers |
| **Technology** | Go |
| **Communication** | WebSocket (clients), Redis Pub/Sub (cross-pod) |
| **Scaling** | Horizontal with sticky sessions |
| **Data** | Owns MongoDB database |
| **Bounded-Context** | [../02-bounded-contexts/chat-service] |

**Constraints:**
- No end-to-end encryption (future enhancement)
- Text-only messages (no attachments)
- Messages only allowed during active rides

---

### 6. Notifications Service

| Attribute | Description |
|-----------|-------------|
| **Purpose** | Aggregate and deliver push, SMS, and email notifications |
| **Technology** | Go |
| **Communication** | Kafka (consumer), HTTP (external providers) |
| **Scaling** | Horizontal, stateless |
| **Data** | Owns PostgreSQL database (delivery logs) |
| **Bounded-Context** | [../02-bounded-contexts/notification-service] |

---

### 7. Tile Server

| Attribute | Description |
|-----------|-------------|
| **Purpose** | Serve map tiles for Poland region |
| **Technology** | TileServer GL (Node.js) with Nginx cache |
| **Communication** | HTTPS (mobile apps) |
| **Scaling** | Horizontal with CDN(tbd here) |
| **Data** | MBTiles file (10-20GB), Cloudflare R2 |
| **Bounded-Context** | [../02-bounded-contexts/tile-server] |

**Key Responsibilities:**
- Serve vector tiles at zooms 0-14
- Provide map style.json (MapLibre)
- Cache hot tiles in Nginx
- Offload to CDN for edge delivery

**Data Ownership:**
- Pre-rendered MBTiles file (Poland)
- Map style configuration

---

### 8. API Gateway

| Attribute | Description |
|-----------|-------------|
| **Purpose** | Single entry point for all client traffic |
| **Technology** | tbd |
| **Communication** | HTTPS (clients), gRPC/HTTP (internal) |
| **Scaling** | Horizontal, stateless |
| **Data** | None (stateless) |

**Key Responsibilities:**
- SSL/TLS termination
- JWT validation
- Rate limiting per user/IP
- Request routing to appropriate services
- Request/response logging

---

### 9. Driver Tracker

| Attribute | Description |
|-----------|-------------|
| **Purpose** | Real-time GPS ingestion, position caching, and broadcast to active rides |
| **Technology** | Go |
| **Communication** | WebSocket (driver apps), Redis (position cache), Kafka (analytics) |
| **Scaling** | Horizontal with sticky sessions |
| **Data** | Writes Redis (positions), reads PostGIS (ride assignments), writes Kafka (events) |
| **Bounded-Context** | [../02-bounded-contexts/driver-tracker] |

---

## Data Stores

| Store | Technology | Purpose | Owned By |
|-------|------------|---------|---------|
| **Trip Store** | PostgreSQL + PostGIS | Routes, rides, driver states | Trip Planner |
| **User Store** | PostgreSQL | Profiles, credentials, verification | Accounts |
| **Ledger** | PostgreSQL | Transactions, wallets, invoices | Payments |
| **Message Store** | MongoDB | Chat messages, rooms | Chat |
| **Notification Logs** | PostgreSQL | Delivery status, templates | Notifications |
| **Cache** | Redis | Active drivers, ride sessions, rate limits | Shared (all services) |
| **Tile Storage** | MBTiles + R2 | Map tiles | Tile Server |

<!-- ---

## Communication Matrix

| Source | Target | Protocol | Pattern | Criticality |
|--------|--------|----------|---------|-------------|
| Mobile Apps | API Gateway | HTTPS | Sync | High |
| API Gateway | Trip Planner | gRPC | Sync | High |
| API Gateway | Accounts | gRPC | Sync | High |
| API Gateway | Payments | gRPC | Sync | Medium |
| API Gateway | Chat | WebSocket | Async | Medium |
| API Gateway | Tile Server | HTTPS | Sync | Medium |
| Trip Planner | RabbitMQ | AMQP | Async | High |
| RabbitMQ | Route-Calc | AMQP | Async | High |
| Route-Calc | Trip Planner | gRPC | Sync | High |
| Trip Planner | PostGIS | PostgreSQL | Sync | High |
| Route-Calc | PostGIS | PostgreSQL | Sync (read-only) | High |
| Trip Planner | Redis | RESP | Sync | High |
| Route-Calc | Redis | RESP | Sync | Medium |
| All Services | Kafka | Kafka Protocol | Async | Medium |
| Kafka | Notifications | Kafka Protocol | Async | Medium |
| Notifications | Firebase/SMS | HTTPS | Async | Low |
| Payments | Payment Gateway | HTTPS | Sync | High |

---

## Synchronous vs Asynchronous Boundaries

| Operation | Pattern | Rationale |
|-----------|---------|----------|
| Ride request | Async (queue) | Matching is compute-heavy, can be deferred |
| Driver position update | Sync (Redis) | Real-time tracking requires immediate state |
| Payment processing | Sync (with retry) | User expects immediate confirmation |
| Chat messages | Async (WebSocket) | Real-time but doesn't block other operations |
| Notifications | Async (Kafka) | Fire-and-forget, delivery not critical to flow |
| Route registration | Sync | Immediate acknowledgment needed | -->