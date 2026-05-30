package service

import (
	"context"
	"encoding/json"
	"errors"
	"sync"
	"testing"

	"notifications/internal/model"
)

// --- mocks ---

type fakeIdem struct {
	mu        sync.Mutex
	processed map[string]bool
	isErr     error
	markErr   error
	markCalls int
}

func newFakeIdem() *fakeIdem { return &fakeIdem{processed: map[string]bool{}} }

func (f *fakeIdem) IsProcessed(_ context.Context, id string) (bool, error) {
	if f.isErr != nil {
		return false, f.isErr
	}
	f.mu.Lock()
	defer f.mu.Unlock()
	return f.processed[id], nil
}

func (f *fakeIdem) MarkProcessed(_ context.Context, id string) error {
	if f.markErr != nil {
		return f.markErr
	}
	f.mu.Lock()
	defer f.mu.Unlock()
	f.processed[id] = true
	f.markCalls++
	return nil
}

type fakeSse struct {
	mu   sync.Mutex
	sent []string // userIDs
}

func (f *fakeSse) Send(userID string, _ model.Envelope) {
	f.mu.Lock()
	defer f.mu.Unlock()
	f.sent = append(f.sent, userID)
}

func (f *fakeSse) users() []string {
	f.mu.Lock()
	defer f.mu.Unlock()
	out := append([]string(nil), f.sent...)
	return out
}

type fakePusher struct {
	mu     sync.Mutex
	pushed []string
	err    error
}

func (f *fakePusher) Notify(_ context.Context, userID string, _ model.Envelope) error {
	f.mu.Lock()
	defer f.mu.Unlock()
	f.pushed = append(f.pushed, userID)
	return f.err
}

func (f *fakePusher) users() []string {
	f.mu.Lock()
	defer f.mu.Unlock()
	return append([]string(nil), f.pushed...)
}

// --- helpers ---

func envelope(t *testing.T, id, evType string, payload any) model.Envelope {
	t.Helper()
	raw, err := json.Marshal(payload)
	if err != nil {
		t.Fatalf("marshal payload: %v", err)
	}
	return model.Envelope{EventId: id, EventType: evType, Payload: raw}
}

func newTestDispatcher() (*Dispatcher, *fakeIdem, *fakeSse, *fakePusher) {
	idem := newFakeIdem()
	sse := &fakeSse{}
	push := &fakePusher{}
	return NewDispatcher(idem, sse, push), idem, sse, push
}

func setEqual(a, b []string) bool {
	if len(a) != len(b) {
		return false
	}
	m := map[string]int{}
	for _, x := range a {
		m[x]++
	}
	for _, x := range b {
		m[x]--
	}
	for _, v := range m {
		if v != 0 {
			return false
		}
	}
	return true
}

// --- routing table ---

func TestDispatchRouting(t *testing.T) {
	cases := []struct {
		name    string
		evType  string
		payload any
		want    []string
	}{
		{"route_selected→driver", model.EventRouteSelected,
			model.RouteSelectedPayload{DriverId: "D", PassengerId: "P"}, []string{"D"}},
		{"match_confirmed→passenger", model.EventMatchConfirmed,
			model.MatchConfirmedPayload{DriverId: "D", PassengerId: "P"}, []string{"P"}},
		{"match_declined→passenger", model.EventMatchDeclined,
			model.MatchDeclinedPayload{DriverId: "D", PassengerId: "P"}, []string{"P"}},
		{"driver_arrived→passenger", model.EventDriverArrived,
			model.DriverArrivedPayload{DriverId: "D", PassengerId: "P"}, []string{"P"}},
		{"ride_started→both", model.EventRideStarted,
			model.RideStartedPayload{DriverId: "D", PassengerId: "P"}, []string{"D", "P"}},
		{"ride_completed→both", model.EventRideCompleted,
			model.RideCompletedPayload{DriverId: "D", PassengerId: "P"}, []string{"D", "P"}},
		{"ride_interrupted→passenger", model.EventRideInterrupted,
			model.RideInterruptedPayload{DriverId: "D", PassengerId: "P"}, []string{"P"}},
		{"rating_prompt→both", model.EventRatingPrompt,
			model.RatingPromptPayload{DriverId: "D", PassengerId: "P"}, []string{"D", "P"}},
		{"cancelled_by_passenger→driver", model.EventRideCancelled,
			model.RideCancelledPayload{DriverId: "D", PassengerId: "P", CancelledBy: "passenger"}, []string{"D"}},
		{"cancelled_by_driver→passenger", model.EventRideCancelled,
			model.RideCancelledPayload{DriverId: "D", PassengerId: "P", CancelledBy: "driver"}, []string{"P"}},
		{"cancelled_by_system→both", model.EventRideCancelled,
			model.RideCancelledPayload{DriverId: "D", PassengerId: "P", CancelledBy: "system"}, []string{"D", "P"}},
	}

	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			d, idem, sse, push := newTestDispatcher()
			env := envelope(t, "e-"+tc.name, tc.evType, tc.payload)

			if err := d.Dispatch(context.Background(), env); err != nil {
				t.Fatalf("Dispatch: %v", err)
			}
			if !setEqual(sse.users(), tc.want) {
				t.Errorf("SSE recipients = %v, want %v", sse.users(), tc.want)
			}
			if !setEqual(push.users(), tc.want) {
				t.Errorf("push recipients = %v, want %v", push.users(), tc.want)
			}
			if idem.markCalls != 1 {
				t.Errorf("MarkProcessed calls = %d, want 1", idem.markCalls)
			}
		})
	}
}

func TestDispatchSkipsProcessed(t *testing.T) {
	d, idem, sse, push := newTestDispatcher()
	idem.processed["dup"] = true

	env := envelope(t, "dup", model.EventRideStarted,
		model.RideStartedPayload{DriverId: "D", PassengerId: "P"})

	if err := d.Dispatch(context.Background(), env); err != nil {
		t.Fatalf("Dispatch: %v", err)
	}
	if len(sse.users()) != 0 || len(push.users()) != 0 {
		t.Errorf("expected no delivery for already-processed event")
	}
	if idem.markCalls != 0 {
		t.Errorf("MarkProcessed should not be called for processed event")
	}
}

func TestDispatchMarksOnSecondDelivery(t *testing.T) {
	d, _, sse, _ := newTestDispatcher()
	env := envelope(t, "e1", model.EventRideStarted,
		model.RideStartedPayload{DriverId: "D", PassengerId: "P"})

	if err := d.Dispatch(context.Background(), env); err != nil {
		t.Fatal(err)
	}
	// second time it's deduped
	if err := d.Dispatch(context.Background(), env); err != nil {
		t.Fatal(err)
	}
	if got := len(sse.users()); got != 2 {
		t.Fatalf("expected 2 SSE sends across two dispatches (one deduped), got %d", got)
	}
}

func TestDispatchPushErrorStillMarksProcessed(t *testing.T) {
	d, idem, sse, push := newTestDispatcher()
	push.err = errors.New("fcm down")

	env := envelope(t, "e1", model.EventRideStarted,
		model.RideStartedPayload{DriverId: "D", PassengerId: "P"})

	if err := d.Dispatch(context.Background(), env); err != nil {
		t.Fatalf("push failure must not fail Dispatch: %v", err)
	}
	if len(sse.users()) != 2 {
		t.Errorf("SSE should still fire despite push error")
	}
	if idem.markCalls != 1 {
		t.Errorf("event must be marked processed despite push error (no replay)")
	}
}

func TestDispatchIsProcessedErrorPropagates(t *testing.T) {
	d, idem, _, _ := newTestDispatcher()
	idem.isErr = errors.New("db down")

	env := envelope(t, "e1", model.EventRideStarted,
		model.RideStartedPayload{DriverId: "D", PassengerId: "P"})

	if err := d.Dispatch(context.Background(), env); err == nil {
		t.Fatal("expected error when IsProcessed fails")
	}
}

func TestDispatchUnknownEventTypeIsNoopButMarks(t *testing.T) {
	d, idem, sse, push := newTestDispatcher()
	env := envelope(t, "e1", "totally_unknown", map[string]string{})

	if err := d.Dispatch(context.Background(), env); err != nil {
		t.Fatalf("Dispatch: %v", err)
	}
	if len(sse.users()) != 0 || len(push.users()) != 0 {
		t.Errorf("unknown event should deliver to nobody")
	}
	if idem.markCalls != 1 {
		t.Errorf("unknown event should still be marked processed")
	}
}

func TestDispatchMalformedPayloadErrors(t *testing.T) {
	d, _, _, _ := newTestDispatcher()
	env := model.Envelope{EventId: "e1", EventType: model.EventRideStarted, Payload: json.RawMessage(`{not json`)}

	if err := d.Dispatch(context.Background(), env); err == nil {
		t.Fatal("expected error on malformed payload")
	}
}