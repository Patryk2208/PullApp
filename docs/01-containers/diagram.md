```mermaid
---
title: PullApp - Container Architecture
---
graph TB
    subgraph Clients["Client Layer | React Native"]
        direction LR
        Driver["<b>Driver Interface</b><br/>GPS Tracking<br/>Route Display"]
        Passenger["<b>Passenger Interface</b><br/>Ride Request<br/>Real-time Updates"]
    end

    subgraph Gateway["API Gateway"]
        API["<b>Entry Point</b><br/>Authentication<br/>Rate Limiting<br/>Load Balancing<br/>SSL Termination"]
    end

    subgraph Core["Core Services"]
        direction TB
        
        TripPlanner["<b>Trip Planner</b><br/>.NET<br/>Ride Orchestration<br/>State Management<br/>Route Registration"]
        TripPlannerDb[("<b>Trip Store</b><br/>PostgreSQL + PostGIS<br/><br/>Routes · Rides<br/>Driver States<br/>Spatial Indexes")]
        
        ComputeQueue[("<b>Compute Queue</b><br/>RabbitMQ<br/>")]

        ResultsQueue[("<b>Results Queue</b><br/>RabbitMQ<br/>")]
        
        subgraph Compute["Compute Cluster | Auto-scaled"]
            RouteCalc["<b>Route-Calc</b><br/>C++20 + OSRM<br/><br/>Queue Consumer<br/>Distance Matrix<br/>Driver Scoring<br/>Embedded Routing"]
        end
    end

    subgraph RealTime["Real-Time Services"]
        direction LR
        DriverTracker["<b>Driver Tracker</b><br/>Go<br/><br/>WebSocket Hub<br/>GPS Ingestion<br/>Geospatial Index"]
    end

    subgraph AccountService["Account Service"]
        direction LR
        Accounts["<b>Accounts</b><br/>.NET<br/>User Registration<br/>Authentication<br/>Document Verification<br/>Rating Management"]
        AccountsDb[(<b>User Store</b><br/>PostgreSQL<br/><br/>Users · Profiles<br/>Credentials<br/>Verification Docs)]
    end

    subgraph PaymentService["Payment Service"]
        direction LR
        Payments["<b>Payments</b><br/>.NET<br/>Transaction Processing<br/>Wallet Management<br/>Invoicing<br/>Settlement"]
        PaymentsDb[(<b>Ledger</b><br/>PostgreSQL<br/><br/>Transactions<br/>Wallets · Invoices<br/>Audit Logs)]
    end

    subgraph ChatService["Chat Service"]
        direction LR
        Chat["<b>Chat</b><br/>Go<br/>WebSocket Hub<br/>Room-based Routing<br/>Message Delivery<br/>Offline Queue"]
        ChatDb[(<b>Message Store</b><br/>MongoDB<br/><br/>Messages · Rooms<br/>Retention: 30d<br/>No E2E Encryption)]
    end

    subgraph TileService["Map Tile Service"]
        direction LR   
        TileServer["<b>Tile Server</b><br/>TileServer GL(Node.js)<br/><br/>Vector Tiles<br/>MBTiles<br/>Style.json"]
    end

    subgraph Events["Event Bus"]
        EventQueue["<b>Event Queue</b><br/>Kafka<br/>ride-completions<br/>user-actions<br/>notification-triggers<br/>analytics-events"]
    end

    subgraph NotificationService["Notification Service"]
        Notifications["<b>Notifications</b><br/>Go<br/>Push (FCM)<br/>SMS Gateway<br/>Email Service<br/>Template Management"]
    end

    subgraph Cache["Cache Layer"]
        direction LR
        Redis[(<b>Redis</b><br/>Active Drivers<br/>Ride Sessions<br/>Rate Limits<br/>Route Cache)]
    end

    subgraph External["External Systems"]
        direction LR
        PaymentGateway[("Payment Gateway<br/>Stripe/Przelewy24")]
        OSM[("OSM Data<br/>Poland Extract")]
    end

    %% Connections
    Clients --> API
    API --> TripPlanner
    API --> Accounts
    API --> Payments
    API <--> Chat
    API --> TileServer
    API <---> DriverTracker
    
    TripPlanner --> TripPlannerDb
    TripPlanner --> Redis
    TripPlanner --> ComputeQueue
    
    ComputeQueue --> RouteCalc
    RouteCalc --> TripPlannerDb
    RouteCalc --> Redis
    RouteCalc --> OSM
    RouteCalc --> ResultsQueue
    ResultsQueue --> TripPlanner
    
    Accounts --> AccountsDb
    Accounts --> Redis
    
    Payments --> PaymentsDb
    Payments --> PaymentGateway
    
    Chat --> ChatDb
    Chat --> Redis
    
    TripPlanner --> EventQueue
    Accounts --> EventQueue
    Payments --> EventQueue
    Chat --> EventQueue
    
    EventQueue --> Notifications
    Notifications --> Clients

    DriverTracker --> Redis
    DriverTracker --> TripPlannerDb
    DriverTracker --> EventQueue