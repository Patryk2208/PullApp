package model

import (
	"encoding/json"
	"fmt"
	"sync"
	"sync/atomic"
	"testing"
	"time"
)

func TestRegisterSendDeliver(t *testing.T) {
	m := NewUsersMapper()
	ch := m.Register("u1")
	defer m.Unregister("u1", ch)

	want := Notification{Type: "ride_started", Payload: json.RawMessage(`{"x":1}`)}
	m.Send("u1", want)

	select {
	case got := <-ch:
		if got.Type != want.Type || string(got.Payload) != string(want.Payload) {
			t.Fatalf("got %+v, want %+v", got, want)
		}
	case <-time.After(time.Second):
		t.Fatal("timed out waiting for notification")
	}
}

func TestSendUnknownUserIsNoop(t *testing.T) {
	m := NewUsersMapper()
	// must not panic or block
	m.Send("ghost", Notification{Type: "x"})
}

func TestUnregisterClosesChannel(t *testing.T) {
	m := NewUsersMapper()
	ch := m.Register("u1")
	m.Unregister("u1", ch)

	select {
	case _, ok := <-ch:
		if ok {
			t.Fatal("expected closed channel, got a value")
		}
	case <-time.After(time.Second):
		t.Fatal("expected closed channel, but read blocked")
	}

	// sending after unregister is a no-op (user gone from the map)
	m.Send("u1", Notification{Type: "x"})
}

func TestDuplicateRegisterClosesOldChannel(t *testing.T) {
	m := NewUsersMapper()
	old := m.Register("u1")
	fresh := m.Register("u1") // second tab replaces the first

	select {
	case _, ok := <-old:
		if ok {
			t.Fatal("expected old channel closed")
		}
	case <-time.After(time.Second):
		t.Fatal("old channel was not closed on duplicate register")
	}

	// the fresh channel is the live one
	m.Send("u1", Notification{Type: "live"})
	select {
	case got := <-fresh:
		if got.Type != "live" {
			t.Fatalf("got %q on fresh channel", got.Type)
		}
	case <-time.After(time.Second):
		t.Fatal("fresh channel did not receive")
	}
}

func TestStaleUnregisterDoesNotTouchNewConnection(t *testing.T) {
	m := NewUsersMapper()
	old := m.Register("u1")
	fresh := m.Register("u1") // closes old, owns the slot

	// the old handler's deferred Unregister fires late — it must NOT remove fresh
	m.Unregister("u1", old)

	m.Send("u1", Notification{Type: "live"})
	select {
	case got, ok := <-fresh:
		if !ok {
			t.Fatal("fresh channel was wrongly closed by stale unregister")
		}
		if got.Type != "live" {
			t.Fatalf("got %q", got.Type)
		}
	case <-time.After(time.Second):
		t.Fatal("fresh channel did not receive after stale unregister")
	}
}

func TestSendDropsWhenFull(t *testing.T) {
	m := NewUsersMapper()
	ch := m.Register("u1")
	defer m.Unregister("u1", ch)

	// fill the 32-slot buffer plus extra; extras must be dropped, never block
	done := make(chan struct{})
	go func() {
		for i := range 1000 {
			m.Send("u1", Notification{Type: fmt.Sprintf("n%d", i)})
		}
		close(done)
	}()

	select {
	case <-done:
	case <-time.After(2 * time.Second):
		t.Fatal("Send blocked on a full channel instead of dropping")
	}
}

// TestConcurrentRegisterSendUnregister is meant to be run with -race.
func TestConcurrentRegisterSendUnregister(t *testing.T) {
	m := NewUsersMapper()
	const users = 50
	const rounds = 200

	var wg sync.WaitGroup

	// senders hammering the whole keyspace
	for range 8 {
		wg.Go(func() {
			for i := range rounds {
				m.Send(fmt.Sprintf("u%d", i%users), Notification{Type: "x"})
			}
		})
	}

	// connections registering, draining, and unregistering
	for u := range users {
		wg.Go(func() {
			id := fmt.Sprintf("u%d", u)
			for range rounds {
				ch := m.Register(id)
				// drain whatever is buffered without blocking
				draining := true
				for draining {
					select {
					case <-ch:
					default:
						draining = false
					}
				}
				m.Unregister(id, ch)
			}
		})
	}

	wg.Wait()
}

// TestLoadFanout is a small load test: many connected users, many concurrent
// senders, for a fixed duration. It asserts the hub never blocks a sender and
// reports delivered-vs-dropped so you can eyeball backpressure behaviour.
// Run with: go test -race -run TestLoadFanout ./internal/model/
func TestLoadFanout(t *testing.T) {
	if testing.Short() {
		t.Skip("skipping load test in -short mode")
	}

	m := NewUsersMapper()
	const users = 500
	const senders = 16
	const duration = 500 * time.Millisecond

	var delivered atomic.Int64

	// each user has a connection draining its channel
	stop := make(chan struct{})
	var consumers sync.WaitGroup
	chans := make([]<-chan Notification, users)
	for u := range users {
		id := fmt.Sprintf("u%d", u)
		ch := m.Register(id)
		chans[u] = ch
		consumers.Go(func() {
			for {
				select {
				case <-stop:
					return
				case _, ok := <-ch:
					if !ok {
						return
					}
					delivered.Add(1)
				}
			}
		})
	}

	// senders push for the whole duration; Send must never block
	var sent atomic.Int64
	deadline := time.Now().Add(duration)
	var producers sync.WaitGroup
	for range senders {
		producers.Go(func() {
			i := 0
			for time.Now().Before(deadline) {
				m.Send(fmt.Sprintf("u%d", i%users), Notification{Type: "load"})
				sent.Add(1)
				i++
			}
		})
	}

	doneSending := make(chan struct{})
	go func() { producers.Wait(); close(doneSending) }()

	select {
	case <-doneSending:
	case <-time.After(duration + 5*time.Second):
		t.Fatal("senders blocked — Send is not non-blocking under load")
	}

	close(stop)
	for u := range users {
		m.Unregister(fmt.Sprintf("u%d", u), chans[u])
	}
	consumers.Wait()

	s, d := sent.Load(), delivered.Load()
	t.Logf("load: sent=%d delivered=%d dropped=%d (%.1f%% delivered)",
		s, d, s-d, 100*float64(d)/float64(s))
	if s == 0 {
		t.Fatal("no messages were sent")
	}
}