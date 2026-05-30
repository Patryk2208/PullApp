package model

import (
	"encoding/json"
	"time"
)

// Event type constants — match trip-planner's EventType string values.
const (
	EventRideCompleted   = "ride_completed"
	EventRideCancelled   = "ride_cancelled"
	EventRideInterrupted = "ride_interrupted"
	EventRouteSelected   = "route_selected"
	EventMatchConfirmed  = "match_confirmed"
	EventMatchDeclined   = "match_declined"
	EventDriverArrived   = "driver_arrived"
	EventRideStarted     = "ride_started"
	EventRatingPrompt    = "rating_prompt"
)

// Topic constants — match trip-planner's Topics class.
const (
	TopicRideCompletions      = "ride-completions"
	TopicUserActions          = "user-actions"
	TopicNotificationTriggers = "notification-triggers"
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

// ── ride-completions ──────────────────────────────────────────────────────────

type RideCompletedPayload struct {
	RideId            string    `json:"RideId"`
	DriverId          string    `json:"DriverId"`
	PassengerId       string    `json:"PassengerId"`
	FrozenPriceId     string    `json:"FrozenPriceId"`
	FrozenPriceAmount float64   `json:"FrozenPriceAmount"`
	DistanceMeters    int       `json:"DistanceMeters"`
	DurationSeconds   int       `json:"DurationSeconds"`
	CompletedAt       time.Time `json:"CompletedAt"`
}

type RideCancelledPayload struct {
	RideId            string    `json:"RideId"`
	DriverId          string    `json:"DriverId"`
	PassengerId       string    `json:"PassengerId"`
	FrozenPriceId     *string   `json:"FrozenPriceId"`
	CancelledBy       string    `json:"CancelledBy"`
	CancellationPhase string    `json:"CancellationPhase"`
	CancelledAt       time.Time `json:"CancelledAt"`
}

type RideInterruptedPayload struct {
	RideId        string    `json:"RideId"`
	DriverId      string    `json:"DriverId"`
	PassengerId   string    `json:"PassengerId"`
	FrozenPriceId *string   `json:"FrozenPriceId"`
	InterruptedAt time.Time `json:"InterruptedAt"`
}

// ── user-actions ──────────────────────────────────────────────────────────────

type RouteSelectedPayload struct {
	RequestId             string    `json:"RequestId"`
	DriverId              string    `json:"DriverId"`
	PassengerId           string    `json:"PassengerId"`
	PassengerDisplayName  string    `json:"PassengerDisplayName"`
	PickupPoint           GeoPoint  `json:"PickupPoint"`
	DropoffPoint          GeoPoint  `json:"DropoffPoint"`
	EtaToPassengerSeconds int       `json:"EtaToPassengerSeconds"`
	ExpiresAt             time.Time `json:"ExpiresAt"`
}

type MatchConfirmedPayload struct {
	RideId      string `json:"RideId"`
	DriverId    string `json:"DriverId"`
	PassengerId string `json:"PassengerId"`
}

type MatchDeclinedPayload struct {
	RequestId   string `json:"RequestId"`
	DriverId    string `json:"DriverId"`
	PassengerId string `json:"PassengerId"`
}

type DriverArrivedPayload struct {
	RideId      string `json:"RideId"`
	DriverId    string `json:"DriverId"`
	PassengerId string `json:"PassengerId"`
}

type RideStartedPayload struct {
	RideId      string    `json:"RideId"`
	DriverId    string    `json:"DriverId"`
	PassengerId string    `json:"PassengerId"`
	StartedAt   time.Time `json:"StartedAt"`
}

// ── notification-triggers ─────────────────────────────────────────────────────

type RatingPromptPayload struct {
	RideId      string `json:"RideId"`
	DriverId    string `json:"DriverId"`
	PassengerId string `json:"PassengerId"`
}