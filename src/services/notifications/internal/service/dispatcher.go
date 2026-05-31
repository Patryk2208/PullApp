package service

import (
	"context"
	"fmt"
	"log"
	"strings"

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
		log.Printf("dispatch: ClaimEvent failed eventId=%s: %v", envelope.EventId, err)
		return err
	}
	if !claimed {
		log.Printf("dispatch: skipping duplicate eventId=%s type=%s", envelope.EventId, envelope.EventType)
		return nil
	}

	users, err := recipients(envelope)
	if err != nil {
		return fmt.Errorf("resolve recipients for %s: %w", envelope.EventType, err)
	}

	if len(users) == 0 {
		log.Printf("dispatch: no recipients for type=%s eventId=%s", envelope.EventType, envelope.EventId)
		return nil
	}

	log.Printf("dispatch: routing type=%s eventId=%s to [%s]",
		envelope.EventType, envelope.EventId, strings.Join(users, ", "))

	for _, userID := range users {
		if userID == "" {
			continue
		}
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

	// ── notification-triggers ────────────────────────────────────────────────

	case model.EventRideRequested:
		p, err := model.DecodePayload[model.RideRequestedPayload](env)
		if err != nil {
			return nil, err
		}
		return []string{p.DriverId}, nil

	case model.EventRideRejected:
		p, err := model.DecodePayload[model.RideRejectedPayload](env)
		if err != nil {
			return nil, err
		}
		return []string{p.PassengerId}, nil

	case model.EventRideAccepted:
		p, err := model.DecodePayload[model.RideAcceptedPayload](env)
		if err != nil {
			return nil, err
		}
		return []string{p.PassengerId}, nil

	case model.EventRouteReady:
		p, err := model.DecodePayload[model.RouteReadyPayload](env)
		if err != nil {
			return nil, err
		}
		return []string{p.DriverId}, nil

	case model.EventRouteSearchCompleted:
		p, err := model.DecodePayload[model.RouteSearchCompletedPayload](env)
		if err != nil {
			return nil, err
		}
		return []string{p.PassengerId}, nil

	case model.EventRideEnded:
		p, err := model.DecodePayload[model.RideEndedPayload](env)
		if err != nil {
			return nil, err
		}
		return p.NotifyPassengerIds, nil

	case model.EventRouteDeleted:
		p, err := model.DecodePayload[model.RouteDeletedPayload](env)
		if err != nil {
			return nil, err
		}
		return p.AffectedPassengerIds, nil

	// ── ride-completions ─────────────────────────────────────────────────────

	case model.EventRideCompleted:
		p, err := model.DecodePayload[model.RideCompletedPayload](env)
		if err != nil {
			return nil, err
		}
		return []string{p.PassengerId, p.DriverId}, nil

	case model.EventRideCancelled:
		p, err := model.DecodePayload[model.RideCancelledPayload](env)
		if err != nil {
			return nil, err
		}
		switch p.CancelledBy {
		case "passenger":
			return []string{p.DriverId}, nil
		case "driver":
			return []string{p.PassengerId}, nil
		default:
			return []string{p.PassengerId, p.DriverId}, nil
		}

	default:
		return nil, nil
	}
}
