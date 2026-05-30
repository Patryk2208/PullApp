package service

import (
	"encoding/json"
	"testing"

	"notifications/internal/model"
)

func TestPushContentCoversAllEventTypes(t *testing.T) {
	all := []string{
		model.EventRouteSelected, model.EventMatchConfirmed, model.EventMatchDeclined,
		model.EventDriverArrived, model.EventRideStarted, model.EventRideCompleted,
		model.EventRideCancelled, model.EventRideInterrupted, model.EventRatingPrompt,
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
		model.EventRouteSelected: true, model.EventMatchConfirmed: true,
		model.EventMatchDeclined: true, model.EventDriverArrived: true,
		model.EventRideCancelled: true, model.EventRideInterrupted: true,
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

func TestPushContentUsesPassengerDisplayName(t *testing.T) {
	raw, _ := json.Marshal(model.RouteSelectedPayload{
		DriverId: "D", PassengerId: "P", PassengerDisplayName: "Alice",
	})
	_, body, _, ok := pushContent(model.Envelope{
		EventType: model.EventRouteSelected,
		Payload:   raw,
	})
	if !ok {
		t.Fatal("expected push content")
	}
	if want := "Alice selected your route"; body != want {
		t.Errorf("body = %q, want %q", body, want)
	}
}

func TestPushContentFallsBackOnMissingDisplayName(t *testing.T) {
	// empty/garbage payload must not panic; falls back to generic copy
	_, body, _, ok := pushContent(model.Envelope{
		EventType: model.EventRouteSelected,
		Payload:   json.RawMessage(`{`),
	})
	if !ok {
		t.Fatal("expected push content even with bad payload")
	}
	if want := "A passenger selected your route"; body != want {
		t.Errorf("body = %q, want %q", body, want)
	}
}