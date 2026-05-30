package service

import (
	"context"
	"fmt"
	"log"

	"notifications/internal/model"
)

type IdempotencyRepository interface {
	IsProcessed(ctx context.Context, eventID string) (bool, error)
	MarkProcessed(ctx context.Context, eventID string) error
}

// SseSender Streamer delivers an event to a user's live SSE connection.
type SseSender interface {
	Send(userID string, env model.Envelope)
}

// Pusher delivers an event to a user as a push notification.
type Pusher interface {
	Notify(ctx context.Context, userID string, env model.Envelope) error
}

type Dispatcher struct {
	idempotencyRepository IdempotencyRepository
	streamer              SseSender
	pusher                Pusher
}

func NewDispatcher(repository IdempotencyRepository, streamer SseSender, pusher Pusher) *Dispatcher {
	return &Dispatcher{
		idempotencyRepository: repository,
		streamer:              streamer,
		pusher:                pusher,
	}
}

func (d *Dispatcher) Dispatch(ctx context.Context, envelope model.Envelope) error {
	// idempotency check for notifications
	processed, err := d.idempotencyRepository.IsProcessed(ctx, envelope.EventId)
	if err != nil {
		return err
	}
	if processed {
		return nil
	}

	users, err := recipients(envelope)
	if err != nil {
		return fmt.Errorf("resolve recipients for %s: %w", envelope.EventType, err)
	}

	for _, userID := range users {
		if userID == "" {
			continue
		}
		// SSE is fire-and-forget; push is best-effort. A push failure must not
		// stop the event from being marked processed.
		d.streamer.Send(userID, envelope)
		if err = d.pusher.Notify(ctx, userID, envelope); err != nil {
			log.Printf("dispatch: push to %s for %s: %v", userID, envelope.EventType, err)
		}
	}

	return d.idempotencyRepository.MarkProcessed(ctx, envelope.EventId)
}

// recipients implements the routing table: which user IDs should receive a
// given event, derived from the decoded payload.
func recipients(env model.Envelope) ([]string, error) {
	switch env.EventType {
	case model.EventRouteSelected:
		p, err := model.DecodePayload[model.RouteSelectedPayload](env)
		if err != nil {
			return nil, err
		}
		return []string{p.DriverId}, nil

	case model.EventMatchConfirmed:
		p, err := model.DecodePayload[model.MatchConfirmedPayload](env)
		if err != nil {
			return nil, err
		}
		return []string{p.PassengerId}, nil

	case model.EventMatchDeclined:
		p, err := model.DecodePayload[model.MatchDeclinedPayload](env)
		if err != nil {
			return nil, err
		}
		return []string{p.PassengerId}, nil

	case model.EventDriverArrived:
		p, err := model.DecodePayload[model.DriverArrivedPayload](env)
		if err != nil {
			return nil, err
		}
		return []string{p.PassengerId}, nil

	case model.EventRideStarted:
		p, err := model.DecodePayload[model.RideStartedPayload](env)
		if err != nil {
			return nil, err
		}
		return []string{p.PassengerId, p.DriverId}, nil

	case model.EventRideCompleted:
		p, err := model.DecodePayload[model.RideCompletedPayload](env)
		if err != nil {
			return nil, err
		}
		return []string{p.PassengerId, p.DriverId}, nil

	case model.EventRideInterrupted:
		p, err := model.DecodePayload[model.RideInterruptedPayload](env)
		if err != nil {
			return nil, err
		}
		return []string{p.PassengerId}, nil

	case model.EventRideCancelled:
		p, err := model.DecodePayload[model.RideCancelledPayload](env)
		if err != nil {
			return nil, err
		}
		// notify the OTHER party (the one who did not cancel)
		switch p.CancelledBy {
		case "passenger":
			return []string{p.DriverId}, nil
		case "driver":
			return []string{p.PassengerId}, nil
		default: // "system" → both
			return []string{p.PassengerId, p.DriverId}, nil
		}

	case model.EventRatingPrompt:
		p, err := model.DecodePayload[model.RatingPromptPayload](env)
		if err != nil {
			return nil, err
		}
		return []string{p.PassengerId, p.DriverId}, nil

	default:
		return nil, nil
	}
}
