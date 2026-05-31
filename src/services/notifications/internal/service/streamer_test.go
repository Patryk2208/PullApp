package service

import (
	"encoding/json"
	"testing"
	"time"

	"notifications/internal/model"
)

func TestStreamerConvertsEnvelopeToThinDTO(t *testing.T) {
	m := model.NewUsersMapper()
	s := NewStreamer(m)

	ch := m.Register("u1")
	defer m.Unregister("u1", ch)

	raw := json.RawMessage(`{"RideId":"r1","DriverId":"u1"}`)
	s.Send("u1", model.Envelope{
		EventId:   "e1",
		EventType: model.EventRideCompleted,
		Payload:   raw,
	})

	select {
	case got := <-ch:
		if got.Type != model.EventRideCompleted {
			t.Errorf("Type = %q, want %q", got.Type, model.EventRideCompleted)
		}
		if string(got.Payload) != string(raw) {
			t.Errorf("Payload = %s, want %s (raw passthrough, no re-encode)", got.Payload, raw)
		}
	case <-time.After(time.Second):
		t.Fatal("nothing delivered to channel")
	}
}

func TestStreamerSendToDisconnectedUserIsNoop(t *testing.T) {
	m := model.NewUsersMapper()
	s := NewStreamer(m)
	// no Register — must not panic or block
	s.Send("ghost", model.Envelope{EventType: model.EventRideCompleted})
}