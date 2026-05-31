package model

import (
	"encoding/json"
	"time"
)

// Event type constants — match trip-planner's IDomainEvent.EventType strings exactly.
const (
	// notification-triggers topic
	EventRideRequested        = "ride_requested"
	EventRideRejected         = "ride_rejected"
	EventRideAccepted         = "ride_accepted"
	EventRouteReady           = "route_ready"
	EventRouteSearchCompleted = "route_search_completed"
	EventRideEnded            = "ride_ended"
	EventRouteDeleted         = "route_deleted"
	// ride-completions topic
	EventRideCompleted = "ride_completed"
	EventRideCancelled = "ride_cancelled"
)

// Envelope is the outer wrapper trip-planner publishes for every Kafka event.
// PascalCase JSON tags match .NET's default JsonSerializer behaviour.
type Envelope struct {
	EventId    string          `json:"EventId"`
	EventType  string          `json:"EventType"`
	OccurredAt time.Time       `json:"OccurredAt"`
	Payload    json.RawMessage `json:"Payload"`
}

// DecodePayload deserialises the raw Payload field into T.
func DecodePayload[T any](e Envelope) (T, error) {
	var v T
	err := json.Unmarshal(e.Payload, &v)
	return v, err
}

// GeoPoint matches TripPlanner.Domain.GeoPoint.
type GeoPoint struct {
	Lat float64 `json:"Lat"`
	Lng float64 `json:"Lng"`
}

// ── notification-triggers ─────────────────────────────────────────────────────

type RideRequestedPayload struct {
	RequestId   string   `json:"RequestId"`
	RouteId     string   `json:"RouteId"`
	DriverId    string   `json:"DriverId"`
	PassengerId string   `json:"PassengerId"`
	StartPoint  GeoPoint `json:"StartPoint"`
	EndPoint    GeoPoint `json:"EndPoint"`
}

type RideRejectedPayload struct {
	RequestId   string `json:"RequestId"`
	RouteId     string `json:"RouteId"`
	DriverId    string `json:"DriverId"`
	PassengerId string `json:"PassengerId"`
}

type RideAcceptedPayload struct {
	RideId      string  `json:"RideId"`
	RequestId   string  `json:"RequestId"`
	RouteId     string  `json:"RouteId"`
	DriverId    string  `json:"DriverId"`
	PassengerId string  `json:"PassengerId"`
	ChatRoomId  *string `json:"ChatRoomId"`
}

type RouteReadyPayload struct {
	RouteId         string     `json:"RouteId"`
	DriverId        string     `json:"DriverId"`
	RoutePoints     []GeoPoint `json:"RoutePoints"`
	DistanceMeters  float64    `json:"DistanceMeters"`
	DurationSeconds float64    `json:"DurationSeconds"`
}

type RouteSearchCompletedPayload struct {
	JobId       string `json:"JobId"`
	PassengerId string `json:"PassengerId"`
}

type RideEndedPayload struct {
	RideId             string   `json:"RideId"`
	RouteId            string   `json:"RouteId"`
	DriverId           string   `json:"DriverId"`
	PassengerId        string   `json:"PassengerId"`
	NotifyPassengerIds []string `json:"NotifyPassengerIds"`
}

type RouteDeletedPayload struct {
	RouteId              string   `json:"RouteId"`
	DriverId             string   `json:"DriverId"`
	AffectedPassengerIds []string `json:"AffectedPassengerIds"`
}

// ── ride-completions ──────────────────────────────────────────────────────────

type RideCompletedPayload struct {
	RideId      string `json:"RideId"`
	DriverId    string `json:"DriverId"`
	PassengerId string `json:"PassengerId"`
}

type RideCancelledPayload struct {
	RideId      string `json:"RideId"`
	DriverId    string `json:"DriverId"`
	PassengerId string `json:"PassengerId"`
	CancelledBy string `json:"CancelledBy"`
}