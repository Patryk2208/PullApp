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
	mu         sync.Mutex
	processed  map[string]bool
	claimErr   error
	claimCalls int
}

func newFakeIdem() *fakeIdem { return &fakeIdem{processed: map[string]bool{}} }

func (f *fakeIdem) ClaimEvent(_ context.Context, id string) (bool, error) {
	if f.claimErr != nil {
		return false, f.claimErr
	}
	f.mu.Lock()
	defer f.mu.Unlock()
	if f.processed[id] {
		return false, nil
	}
	f.processed[id] = true
	f.claimCalls++
	return true, nil
}

type fakePublisher struct {
	mu       sync.Mutex
	published []string // userIDs
	err      error
}

func (f *fakePublisher) Publish(_ context.Context, userID string, _ model.Envelope) error {
	f.mu.Lock()
	defer f.mu.Unlock()
	f.published = append(f.published, userID)
	return f.err
}

func (f *fakePublisher) users() []string {
	f.mu.Lock()
	defer f.mu.Unlock()
	return append([]string(nil), f.published...)
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

func newTestDispatcher() (*Dispatcher, *fakeIdem, *fakePublisher, *fakePusher) {
	idem := newFakeIdem()
	pub := &fakePublisher{}
	push := &fakePusher{}
	return NewDispatcher(idem, pub, push), idem, pub, push
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
		{"ride_requested→driver", model.EventRideRequested,
			model.RideRequestedPayload{DriverId: "D", PassengerId: "P"}, []string{"D"}},
		{"ride_rejected→passenger", model.EventRideRejected,
			model.RideRejectedPayload{DriverId: "D", PassengerId: "P"}, []string{"P"}},
		{"ride_accepted→passenger", model.EventRideAccepted,
			model.RideAcceptedPayload{DriverId: "D", PassengerId: "P"}, []string{"P"}},
		{"route_ready→driver", model.EventRouteReady,
			model.RouteReadyPayload{DriverId: "D"}, []string{"D"}},
		{"route_search_completed→passenger", model.EventRouteSearchCompleted,
			model.RouteSearchCompletedPayload{PassengerId: "P"}, []string{"P"}},
		{"ride_ended→notify_passengers", model.EventRideEnded,
			model.RideEndedPayload{NotifyPassengerIds: []string{"P1", "P2"}}, []string{"P1", "P2"}},
		{"route_deleted→affected_passengers", model.EventRouteDeleted,
			model.RouteDeletedPayload{AffectedPassengerIds: []string{"P1", "P2"}}, []string{"P1", "P2"}},
		{"ride_completed→both", model.EventRideCompleted,
			model.RideCompletedPayload{DriverId: "D", PassengerId: "P"}, []string{"D", "P"}},
		{"cancelled_by_passenger→driver", model.EventRideCancelled,
			model.RideCancelledPayload{DriverId: "D", PassengerId: "P", CancelledBy: "passenger"}, []string{"D"}},
		{"cancelled_by_driver→passenger", model.EventRideCancelled,
			model.RideCancelledPayload{DriverId: "D", PassengerId: "P", CancelledBy: "driver"}, []string{"P"}},
		{"cancelled_by_system→both", model.EventRideCancelled,
			model.RideCancelledPayload{DriverId: "D", PassengerId: "P", CancelledBy: "system"}, []string{"D", "P"}},
	}

	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			d, idem, pub, push := newTestDispatcher()
			env := envelope(t, "e-"+tc.name, tc.evType, tc.payload)

			if err := d.Dispatch(context.Background(), env); err != nil {
				t.Fatalf("Dispatch: %v", err)
			}
			if !setEqual(pub.users(), tc.want) {
				t.Errorf("publish recipients = %v, want %v", pub.users(), tc.want)
			}
			if !setEqual(push.users(), tc.want) {
				t.Errorf("push recipients = %v, want %v", push.users(), tc.want)
			}
			if idem.claimCalls != 1 {
				t.Errorf("ClaimEvent calls = %d, want 1", idem.claimCalls)
			}
		})
	}
}

func TestDispatchSkipsProcessed(t *testing.T) {
	d, idem, pub, push := newTestDispatcher()
	idem.processed["dup"] = true

	env := envelope(t, "dup", model.EventRideCompleted,
		model.RideCompletedPayload{DriverId: "D", PassengerId: "P"})

	if err := d.Dispatch(context.Background(), env); err != nil {
		t.Fatalf("Dispatch: %v", err)
	}
	if len(pub.users()) != 0 || len(push.users()) != 0 {
		t.Errorf("expected no delivery for already-processed event")
	}
	if idem.claimCalls != 0 {
		t.Errorf("ClaimEvent should not count for already-processed event")
	}
}

func TestDispatchMarksOnSecondDelivery(t *testing.T) {
	d, _, pub, _ := newTestDispatcher()
	env := envelope(t, "e1", model.EventRideCompleted,
		model.RideCompletedPayload{DriverId: "D", PassengerId: "P"})

	if err := d.Dispatch(context.Background(), env); err != nil {
		t.Fatal(err)
	}
	// second dispatch is a no-op: event already claimed
	if err := d.Dispatch(context.Background(), env); err != nil {
		t.Fatal(err)
	}
	if got := len(pub.users()); got != 2 {
		t.Fatalf("expected 2 publishes on first dispatch, got %d", got)
	}
}

func TestDispatchPushErrorStillMarksProcessed(t *testing.T) {
	d, idem, pub, push := newTestDispatcher()
	push.err = errors.New("fcm down")

	env := envelope(t, "e1", model.EventRideCompleted,
		model.RideCompletedPayload{DriverId: "D", PassengerId: "P"})

	if err := d.Dispatch(context.Background(), env); err != nil {
		t.Fatalf("push failure must not fail Dispatch: %v", err)
	}
	if len(pub.users()) != 2 {
		t.Errorf("publish should still fire despite push error")
	}
	if idem.claimCalls != 1 {
		t.Errorf("event must be claimed despite push error (no replay)")
	}
}

func TestDispatchIsProcessedErrorPropagates(t *testing.T) {
	d, idem, _, _ := newTestDispatcher()
	idem.claimErr = errors.New("db down")

	env := envelope(t, "e1", model.EventRideCompleted,
		model.RideCompletedPayload{DriverId: "D", PassengerId: "P"})

	if err := d.Dispatch(context.Background(), env); err == nil {
		t.Fatal("expected error when IsProcessed fails")
	}
}

func TestDispatchUnknownEventTypeIsNoopButMarks(t *testing.T) {
	d, idem, pub, push := newTestDispatcher()
	env := envelope(t, "e1", "totally_unknown", map[string]string{})

	if err := d.Dispatch(context.Background(), env); err != nil {
		t.Fatalf("Dispatch: %v", err)
	}
	if len(pub.users()) != 0 || len(push.users()) != 0 {
		t.Errorf("unknown event should deliver to nobody")
	}
	if idem.claimCalls != 1 {
		t.Errorf("unknown event should still be claimed")
	}
}

func TestDispatchMalformedPayloadErrors(t *testing.T) {
	d, _, _, _ := newTestDispatcher()
	env := model.Envelope{EventId: "e1", EventType: model.EventRideCompleted, Payload: json.RawMessage(`{not json`)}

	if err := d.Dispatch(context.Background(), env); err == nil {
		t.Fatal("expected error on malformed payload")
	}
}