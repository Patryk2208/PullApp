package service

import (
	"context"
	"fmt"
	"log"

	"notifications/internal/model"
)

type IdempotencyRepository interface {
	ClaimEvent(ctx context.Context, eventID string) (bool, error)
}

// Publisher publishes an event to the fan-out bus (Redis pub/sub).
type Publisher interface {
	Publish(ctx context.Context, userID string, env model.Envelope) error
}

// Pusher delivers an event to a user as a push notification.
type Pusher interface {
	Notify(ctx context.Context, userID string, env model.Envelope) error
}

type Dispatcher struct {
	idempotencyRepository IdempotencyRepository
	publisher             Publisher
	pusher                Pusher
}

func NewDispatcher(repository IdempotencyRepository, publisher Publisher, pusher Pusher) *Dispatcher {
	return &Dispatcher{
		idempotencyRepository: repository,
		publisher:             publisher,
		pusher:                pusher,
	}
}

func (d *Dispatcher) Dispatch(ctx context.Context, envelope model.Envelope) error {
	claimed, err := d.idempotencyRepository.ClaimEvent(ctx, envelope.EventId)
	if err != nil {
		return err
	}
	if !claimed {
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
		// SSE fan-out via Redis pub/sub; push is best-effort. Neither failure
		// must block other recipients or prevent the claim from standing.
		if err = d.publisher.Publish(ctx, userID, envelope); err != nil {
			log.Printf("dispatch: publish to %s for %s: %v", userID, envelope.EventType, err)
		}
		if err = d.pusher.Notify(ctx, userID, envelope); err != nil {
			log.Printf("dispatch: push to %s for %s: %v", userID, envelope.EventType, err)
		}
	}

	return nil
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
