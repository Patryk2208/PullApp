package service

import "context"

// Event is the Kafka message payload. All data needed to build the push must
// be embedded here — this service never calls other services (see docs).
type Event struct {
	ID          string `json:"id"`   // idempotency key
	Type        string `json:"type"` // e.g. "ride.accepted"
	PassengerID string `json:"passengerId"`
	DriverID    string `json:"driverId"`
	DriverName  string `json:"driverName"` // pre-resolved by Trip Planner
	Amount      string `json:"amount"`     // for payment.charged
}

// Repository is the persistence interface Notifier depends on.
type Repository interface {
	IsProcessed(ctx context.Context, eventID string) (bool, error)
	MarkProcessed(ctx context.Context, eventID string) error
	GetDeviceToken(ctx context.Context, userID string) (string, error)
	DeleteDeviceToken(ctx context.Context, userID string) error
}

// Notifier is the core of the service. Handle should implement the full
// delivery pipeline in order:
//
//  1. IsProcessed(event.ID) — ack and return nil if already sent.
//  2. Switch on event.Type to pick recipient (PassengerID or DriverID) and
//     build title/body/priority using the table in the bounded-context doc.
//     Templates are hardcoded here — no DB-backed template system.
//  3. GetDeviceToken(recipientID) — ack and return nil if no token (user
//     never registered a device).
//  4. fcm.Send(ctx, &messaging.Message{Token, Notification, Android, APNS})
//     using firebase.google.com/go/v4/messaging. The FCM client is
//     initialised once at startup from a service-account JSON mounted as a
//     K8s secret and injected into Notifier via NewNotifier.
//  5. On messaging.IsRegistrationTokenNotRegistered(err): DeleteDeviceToken.
//  6. MarkProcessed(event.ID) so redeliveries are no-ops.
type Notifier struct {
	repo Repository
	// fcm  *messaging.Client  — add when Firebase Admin SDK is wired up
}

func NewNotifier(repo Repository) *Notifier {
	return &Notifier{repo: repo}
}

func (n *Notifier) Handle(ctx context.Context, event Event) error {
	// TODO: implement delivery pipeline (see type doc above)
	return nil
}
