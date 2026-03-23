# PullApp - Container Architecture (Level 2)

## Service breakdown

## TODO reference UC folders

### 1. Trip Planner

| Attribute | Description |
|-----------|-------------|
| **Purpose** | Ride orchestration, state management, driver route registration, passenger request handling |
| **Technology** | .NET 10 |
| **Communication** | HTTP/WebSocket (clients), gRPC (services), AMQP (RabbitMQ) |
| **Scaling** | Horizontal, stateless |
| **Data** | Writes to PostGIS, reads/writes Redis |

**Key Responsibilities:**
- Register driver routes and maintain active driver state
- Process passenger ride requests
- Publish matching jobs to RabbitMQ
- Track ride lifecycle (requested → accepted → active → completed)
- Manage WebSocket connections for real-time position updates
- Coordinate with Accounts and Payments services

**Data Ownership:**
- Ride state (active rides, history)
- Driver routes (geometry, estimated duration)
- Ride completion events

---

### 2. Route-Calc

| Attribute | Description |
|-----------|-------------|
| **Purpose** | Compute distance matrices, score drivers, find optimal matches |
| **Technology** | C++20 with linked OSRM library |
| **Communication** | AMQP (RabbitMQ consumer), gRPC (results), HTTP (OSRM internal) |
| **Scaling** | KEDA auto-scaling based on queue depth |
| **Data** | Reads PostGIS (candidate drivers), reads OSM data (in-memory), writes Redis cache |

**Key Responsibilities:**
- Consume match requests from RabbitMQ
- Query candidate drivers from PostGIS
- Calculate distance matrices via OSRM
- Score and rank drivers by route deviation
- Cache frequent route calculations in Redis

**Data Ownership:**
- None (read-only access to PostGIS, cache results only)

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

**Key Responsibilities:**
- User registration and authentication (JWT)
- Driver document verification (license, insurance)
- Rating storage and aggregation
- Profile management
- Blocklist and trust scores

**Data Ownership:**
- User profiles (drivers, passengers, admins)
- Credentials and authentication data
- Verification documents and status
- User ratings

---

### 4. Payment Service

| Attribute | Description |
|-----------|-------------|
| **Purpose** | Financial transaction processing, wallet management, settlement |
| **Technology** | .NET 10 |
| **Communication** | gRPC (internal), HTTP (payment gateways) |
| **Scaling** | Horizontal, stateless |
| **Data** | Owns PostgreSQL database |

**Key Responsibilities:**
- Authorize payment methods (cards, wallet)
- Hold funds during active rides (escrow)
- Process payments after ride completion
- Handle refunds and disputes
- Generate invoices and receipts

**Data Ownership:**
- Payment methods
- Transaction history
- Wallet balances
- Invoices and receipts

---

### 5. Chat Service

| Attribute | Description |
|-----------|-------------|
| **Purpose** | Real-time messaging between drivers and passengers |
| **Technology** | Go |
| **Communication** | WebSocket (clients), Redis Pub/Sub (cross-pod) |
| **Scaling** | Horizontal with sticky sessions |
| **Data** | Owns MongoDB database |

**Key Responsibilities:**
- Manage WebSocket connections per ride
- Route messages to room participants
- Store message history (30-day retention)
- Deliver offline messages via push notifications
- Rate limit messages per user

**Data Ownership:**
- Message history
- Conversation rooms
- Delivery status

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

**Key Responsibilities:**
- Consume notification events from Kafka
- Route to appropriate channels (push, SMS, email)
- Manage notification templates
- Track delivery status
- Handle retries with backoff

**Data Ownership:**
- Notification templates
- Delivery logs
- User notification preferences

---

### 7. Tile Server

| Attribute | Description |
|-----------|-------------|
| **Purpose** | Serve map tiles for Poland region |
| **Technology** | TileServer GL (Node.js) with Nginx cache |
| **Communication** | HTTPS (mobile apps) |
| **Scaling** | Horizontal with CDN(tbd here) |
| **Data** | MBTiles file (10-20GB), Cloudflare R2 |

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
| **Scaling** | Horizontal, stateless (3+ replicas) |
| **Data** | None (stateless) |

**Key Responsibilities:**
- SSL/TLS termination
- JWT validation
- Rate limiting per user/IP
- Request routing to appropriate services
- Request/response logging

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

---

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
| Route registration | Sync | Immediate acknowledgment needed |

---

## Data Flow Patterns

### Request-Response (Synchronous)
- Authentication and authorization
- Driver route registration
- Payment authorization
- Ride status queries

### Event-Driven (Asynchronous)
- Matching computation
- Ride completion fan-out (payments, ratings, analytics)
- Notification delivery
- Chat message broadcasting

### Real-Time (Bidirectional)
- Driver position updates
- Ride tracking
- Chat messaging

---

## Scaling Strategies TBD

| Container | Strategy | Min | Max | Trigger |
|-----------|----------|-----|-----|---------|
| Trip Planner | Horizontal | 3 | 10 | CPU > 70% |
| Route-Calc | Queue-based (KEDA) | 1 | 10 | Queue depth > 10 |
| Accounts | Horizontal | 2 | 5 | CPU > 70% |
| Payments | Horizontal | 2 | 5 | CPU > 70% |
| Chat | Horizontal (sticky) | 2 | 10 | Connections > 1000/pod |
| Notifications | Horizontal | 2 | 5 | Queue depth > 100 |
| Tile Server | Horizontal + CDN | 2 | 3 | Cache miss rate > 10% |
| PostGIS | Vertical + replicas | 1 primary + 2 replicas | - | - |
| Redis | Sentinel cluster | 3 nodes | - | - |
| RabbitMQ | Cluster | 3 nodes | - | - |
| Kafka | Cluster | 3 nodes | 5 | Disk usage > 80% |

---

## Deployment Boundaries

Each container runs independently with:

- **Separate deployment unit** (Kubernetes Deployment or StatefulSet)
- **Independent resource allocation** (CPU, memory, storage)
- **Isolated failure domain** (one service failure does not cascade)
- **Own scaling policy** (scales based on its specific load characteristics)

---

## Critical Paths

### Ride Request (User-Facing Critical)
1. Mobile App → API Gateway → Trip Planner
2. Trip Planner → PostGIS (find candidates)
3. Trip Planner → Redis (get active positions)
4. Trip Planner → RabbitMQ (queue match request)
5. Route-Calc → OSRM (compute matrix)
6. Route-Calc → Trip Planner (return matches)
7. Trip Planner → Notifications (via Kafka)
8. Notifications → Firebase (push to drivers)

### Driver Position Update (Real-Time Critical)
1. Mobile App → API Gateway → Trip Planner (WebSocket)
2. Trip Planner → Redis (update geospatial index)
3. Trip Planner → Kafka (emit for analytics)

**Maximum acceptable latency:** 500ms

### Payment Processing (Financial Critical)
1. Mobile App → API Gateway → Payments
2. Payments → Payment Gateway
3. Payments → PostGIS (record transaction)
4. Payments → Kafka (emit completion event)

---

## Constraints & Guardrails

| Constraint | Implementation |
|------------|----------------|
| Route-Calc read-only on PostGIS | Separate database user with SELECT only |
| Chat messages ride-only | API validates active ride before accepting messages |
| Redis data ephemeral | All critical state also persisted in PostGIS |
| Match request TTL | 30 seconds, dead letter queue for monitoring |
| No direct database access between services | All cross-service reads via gRPC APIs |
| Rate limiting | API Gateway enforces per-user limits |