```mermaid
---
title: PullApp Container Architecture
---
graph TB
    subgraph Clients["Client Layer | React Native"]
        direction TB
        Driver["<b>Driver Interface</b>"]
        Passenger["<b>Passenger Interface</b>"]
    end

    subgraph Gateway["API Gateway"]
        API["<br/>Authentication<br/>Rate Limiting<br/>Load Balancing"]
    end

    subgraph Core["Core Flow"]
        direction TB
        
        TripPlanner["<b>Trip Planner</b><br/>"]
        TripPlannerDb[("<b>Trip Planner Database</b></br>PostGis")]
        
        RabbitMQ[(<b>RabbitMQ</b><br/>──────────<br/>Exchange: ride-matches<br/>Queue: match-requests<br/>Priority: premium-first<br/>TTL: 30 seconds<br/>Dead Letter: failed)]
        
        subgraph Compute["Compute Services"]
            RouteCalcN["<b>Route-Calc</b><br/>Auto-scaled by KEDA<br/>───────<br/>Queue Consumer<br/>gRPC Server<br/>Embedded OSRM"]
        end
    end

    subgraph AccountService["Account Service"]
        direction LR
        Accounts["<b>Accounts</b><br/>Auth | Users<br/>Verification<br/>Rating"]
        AccountsDb[(<b>Account Database</b><br/>Postgres)]
    end

    subgraph PaymentService["Payment Service"]
        direction LR
        Payments["<b>Payments</b><br/>Transactions<br/>Wallet"]
        PaymentsDb[(<b>Payments Database</b><br/>Postgres)]
    end

    subgraph ChatService["Chat Service"]
        direction LR
        Chat["<b>Chat</b><br/>WebSocket<br/>Real-time"]
        ChatDb[(<b>Chat History</b><br/>!!! no e2e encryption for now<br/>MongoDB)]
    end

    subgraph TileService["Tile Service"]
        direction LR   
        TileServer["<b>Tile Server</b><br/>TileServer GL<br/>MBTiles<br/>Poland Maps"]
    end

    subgraph Events["Events"]
        EventQueue["<b>Event Queue</b><br/>Kafka"]
    end

    subgraph NotificationService["Notification Services"]
        Notifications["<b>Notifications</b><br/>Push | SMS | Email"]
    end

    subgraph Cache["Cache"]
        direction LR
        Redis[(<b>Redis)]
    end

    subgraph External["External Services"]
        direction LR
        PaymentGateway[("Payment Gateway")]
        OSM[("OSM Data<br/>Poland Extract<br/>500MB PBF")]
    end

    %% Connections
    Clients --> API
    API --> TripPlanner
    
    API --> Accounts
    Accounts --> AccountsDb

    API --> Payments
    Payments --> PaymentsDb
    Payments --> PaymentGateway

    API --> Chat
    Chat --> ChatDb
    
    API --> TileServer
    
    TripPlanner --> TripPlannerDb
    TripPlanner --> RabbitMQ
    RabbitMQ --> RouteCalcN
    RouteCalcN --> TripPlannerDb
    Compute --> OSM

    TripPlanner --> EventQueue
    Accounts --> EventQueue
    Payments --> EventQueue

    EventQueue --> Notifications
    Notifications --> Clients
    
    
    %%style TripPlanner fill:#e1f5fe,stroke:#01579b,stroke-width:2px
    %%style RouteCalc1 fill:#fff3e0,stroke:#e65100,stroke-width:2px
    %%style RouteCalc2 fill:#fff3e0,stroke:#e65100,stroke-width:2px
    %%style RouteCalcN fill:#fff3e0,stroke:#e65100,stroke-width:2px
    %%style PostGIS fill:#e8f5e9,stroke:#1b5e20,stroke-width:2px
    %%style Redis fill:#ffebee,stroke:#b71c1c,stroke-width:2px
    %%style RabbitMQ fill:#f3e5f5,stroke:#4a148c,stroke-width:2px