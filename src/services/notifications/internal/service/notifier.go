package service

import (
	"context"
	"errors"
	"fmt"

	"firebase.google.com/go/v4/messaging"
	"github.com/jackc/pgx/v5"

	"notifications/internal/model"
)

type Repository interface {
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

// Notify renders push content for the event and delivers it to userID's device.
// No-op if the event has no push representation or the user has no device token.
func (n *Notifier) Notify(ctx context.Context, userID string, env model.Envelope) error {
	title, body, priority, ok := pushContent(env)
	if !ok {
		return nil
	}

	token, err := n.repo.GetDeviceToken(ctx, userID)
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
		// device token is dead — drop it so we stop trying
		_ = n.repo.DeleteDeviceToken(ctx, userID)
		return nil
	}
	if err != nil {
		return fmt.Errorf("fcm send: %w", err)
	}
	return nil
}

// pushContent maps an event to a user-facing push title/body/priority. Returns
// ok=false for events that should not produce a push.
func pushContent(env model.Envelope) (title, body, priority string, ok bool) {
	switch env.EventType {
	case model.EventRideRequested:
		return "New ride request", "A passenger requested your route", "high", true
	case model.EventRideAccepted:
		return "Ride confirmed", "Your driver accepted the request", "high", true
	case model.EventRideRejected:
		return "Request declined", "The driver declined your request", "high", true
	case model.EventRouteReady:
		return "Route ready", "Your route has been calculated", "normal", true
	case model.EventRouteSearchCompleted:
		return "Matches found", "Available routes found for your trip", "normal", true
	case model.EventRideEnded:
		return "Seat available", "A ride you were waitlisted for may have a free seat", "normal", true
	case model.EventRouteDeleted:
		return "Route cancelled", "The driver cancelled the route", "high", true
	case model.EventRideCompleted:
		return "Ride complete", "Your ride has ended", "normal", true
	case model.EventRideCancelled:
		return "Ride cancelled", "The ride has been cancelled", "high", true
	default:
		return "", "", "", false
	}
}