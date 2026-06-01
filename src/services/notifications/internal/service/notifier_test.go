package service

import (
	"testing"

	"notifications/internal/model"
)

func TestPushContentCoversAllEventTypes(t *testing.T) {
	all := []string{
		model.EventRideRequested, model.EventRideRejected, model.EventRideAccepted,
		model.EventRouteReady, model.EventRouteSearchCompleted,
		model.EventRideEnded, model.EventRouteDeleted,
		model.EventRideCompleted, model.EventRideCancelled,
	}
	for _, ev := range all {
		t.Run(ev, func(t *testing.T) {
			_, _, _, ok := pushContent(model.Envelope{EventType: ev})
			if !ok {
				t.Errorf("pushContent(%q) ok=false, every known event should have push content", ev)
			}
		})
	}
}

func TestPushContentUnknownEventNoPush(t *testing.T) {
	if _, _, _, ok := pushContent(model.Envelope{EventType: "nope"}); ok {
		t.Error("unknown event should not produce push content")
	}
}

func TestPushContentPriorities(t *testing.T) {
	high := map[string]bool{
		model.EventRideRequested: true,
		model.EventRideRejected:  true,
		model.EventRideAccepted:  true,
		model.EventRouteDeleted:  true,
		model.EventRideCancelled: true,
	}
	for ev, wantHigh := range high {
		_, _, prio, _ := pushContent(model.Envelope{EventType: ev})
		if wantHigh && prio != "high" {
			t.Errorf("%s priority = %q, want high", ev, prio)
		}
	}
	if _, _, prio, _ := pushContent(model.Envelope{EventType: model.EventRideCompleted}); prio != "normal" {
		t.Errorf("ride_completed priority = %q, want normal", prio)
	}
}
