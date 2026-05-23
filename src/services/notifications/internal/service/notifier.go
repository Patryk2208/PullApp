package service

import (
	"context"
	"errors"
	"fmt"

	"firebase.google.com/go/v4/messaging"
	"github.com/jackc/pgx/v5"
)

type Event struct {
	ID          string `json:"id"`
	Type        string `json:"type"`
	PassengerID string `json:"passengerId"`
	DriverID    string `json:"driverId"`
	DriverName  string `json:"driverName"`
	Amount      string `json:"amount"`
}

type Repository interface {
	IsProcessed(ctx context.Context, eventID string) (bool, error)
	MarkProcessed(ctx context.Context, eventID string) error
	GetDeviceToken(ctx context.Context, userID string) (string, error)
	DeleteDeviceToken(ctx context.Context, userID string) error
}

type Notifier struct {
	repo Repository
	fcm  *messaging.Client
}

func NewNotifier(repo Repository, fcm *messaging.Client) *Notifier {
	return &Notifier{repo: repo, fcm: fcm}
}

func (n *Notifier) Handle(ctx context.Context, event Event) error {
	processed, err := n.repo.IsProcessed(ctx, event.ID)
	if err != nil {
		return fmt.Errorf("idempotency check: %w", err)
	}
	if processed {
		return nil
	}

	recipientID, title, body, priority, ok := resolvePayload(event)
	if !ok {
		return nil
	}

	token, err := n.repo.GetDeviceToken(ctx, recipientID)
	if errors.Is(err, pgx.ErrNoRows) {
		return nil
	}
	if err != nil {
		return fmt.Errorf("get device token: %w", err)
	}

	androidPriority := "normal"
	apnsPriority := "5"
	if priority == "high" {
		androidPriority = "high"
		apnsPriority = "10"
	}

	_, err = n.fcm.Send(ctx, &messaging.Message{
		Token: token,
		Notification: &messaging.Notification{
			Title: title,
			Body:  body,
		},
		Android: &messaging.AndroidConfig{
			Priority: androidPriority,
		},
		APNS: &messaging.APNSConfig{
			Headers: map[string]string{
				"apns-priority": apnsPriority,
			},
		},
	})
	if messaging.IsUnregistered(err) {
		_ = n.repo.DeleteDeviceToken(ctx, recipientID)
		_ = n.repo.MarkProcessed(ctx, event.ID)
		return nil
	}
	if err != nil {
		return fmt.Errorf("fcm send: %w", err)
	}

	if err := n.repo.MarkProcessed(ctx, event.ID); err != nil {
		return fmt.Errorf("mark processed: %w", err)
	}
	return nil
}

func resolvePayload(e Event) (recipientID, title, body, priority string, ok bool) {
	switch e.Type {
	case "ride.accepted":
		return e.PassengerID, "Driver accepted", e.DriverName + " is on the way", "high", true
	case "ride.cancelled.by_driver":
		return e.PassengerID, "Ride cancelled", "Your driver cancelled the ride", "high", true
	case "ride.cancelled.by_passenger":
		return e.DriverID, "Ride cancelled", "Passenger cancelled the ride", "high", true
	case "driver.arriving":
		return e.PassengerID, "Driver arriving", e.DriverName + " is almost there", "high", true
	case "ride.completed":
		return e.PassengerID, "Ride complete", "Your ride has ended", "normal", true
	case "payment.charged":
		return e.PassengerID, "Payment confirmed", "Payment of " + e.Amount + " confirmed", "normal", true
	default:
		return "", "", "", "", false
	}
}
