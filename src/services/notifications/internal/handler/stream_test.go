package handler

import (
	"bytes"
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync"
	"testing"
	"time"

	"notifications/internal/model"
)

// syncRecorder is a concurrency-safe ResponseWriter that also implements
// http.Flusher, so the SSE handler (which writes from its own goroutine while
// the test reads) does not race. httptest.ResponseRecorder is not safe for that.
type syncRecorder struct {
	mu   sync.Mutex
	hdr  http.Header
	buf  bytes.Buffer
	code int
}

func newSyncRecorder() *syncRecorder {
	return &syncRecorder{hdr: make(http.Header), code: http.StatusOK}
}

func (s *syncRecorder) Header() http.Header { return s.hdr }

func (s *syncRecorder) Write(p []byte) (int, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.buf.Write(p)
}

func (s *syncRecorder) WriteHeader(code int) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.code = code
}

func (s *syncRecorder) Flush() {}

func (s *syncRecorder) body() string {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.buf.String()
}

func (s *syncRecorder) status() int {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.code
}

// waitRegistered blocks until userID has an open channel in the mapper.
func waitRegistered(t *testing.T, m *model.UsersMapper, userID string) {
	t.Helper()
	deadline := time.After(2 * time.Second)
	for {
		m.Mutex.RLock()
		_, ok := m.UsersMap[userID]
		m.Mutex.RUnlock()
		if ok {
			return
		}
		select {
		case <-deadline:
			t.Fatal("handler never registered the user")
		case <-time.After(time.Millisecond):
		}
	}
}

// waitBody blocks until the recorder body contains want. Fails the test (after
// cancelling) if it does not appear in time.
func waitBody(t *testing.T, rec *syncRecorder, want string, cancel context.CancelFunc, done <-chan struct{}) {
	t.Helper()
	deadline := time.After(2 * time.Second)
	for {
		if strings.Contains(rec.body(), want) {
			return
		}
		select {
		case <-deadline:
			cancel()
			<-done
			t.Fatalf("body never contained %q\nfull body: %q", want, rec.body())
		case <-time.After(time.Millisecond):
		}
	}
}

func TestStreamRejectsMissingUser(t *testing.T) {
	m := model.NewUsersMapper()
	h := NewStreamerHandler(m)

	req := httptest.NewRequest(http.MethodGet, "/stream", nil)
	rec := newSyncRecorder()
	h.ServeHTTP(rec, req)

	if rec.status() != http.StatusUnauthorized {
		t.Fatalf("status = %d, want 401", rec.status())
	}
}

func TestStreamSetsSSEHeadersAndRetry(t *testing.T) {
	m := model.NewUsersMapper()
	h := NewStreamerHandler(m)

	ctx, cancel := context.WithCancel(context.Background())
	req := httptest.NewRequest(http.MethodGet, "/stream", nil).WithContext(ctx)
	req.Header.Set("X-User-Id", "u1")
	rec := newSyncRecorder()

	done := make(chan struct{})
	go func() { h.ServeHTTP(rec, req); close(done) }()

	waitRegistered(t, m, "u1")
	waitBody(t, rec, "retry: 3000", cancel, done)
	cancel()
	<-done

	if ct := rec.Header().Get("Content-Type"); ct != "text/event-stream" {
		t.Errorf("Content-Type = %q", ct)
	}
	if cc := rec.Header().Get("Cache-Control"); cc != "no-cache" {
		t.Errorf("Cache-Control = %q", cc)
	}
	if ab := rec.Header().Get("X-Accel-Buffering"); ab != "no" {
		t.Errorf("X-Accel-Buffering = %q", ab)
	}
}

func TestStreamWritesEventInSSEFormat(t *testing.T) {
	m := model.NewUsersMapper()
	h := NewStreamerHandler(m)

	ctx, cancel := context.WithCancel(context.Background())
	req := httptest.NewRequest(http.MethodGet, "/stream", nil).WithContext(ctx)
	req.Header.Set("X-User-Id", "u1")
	rec := newSyncRecorder()

	done := make(chan struct{})
	go func() { h.ServeHTTP(rec, req); close(done) }()

	waitRegistered(t, m, "u1")
	m.Send("u1", model.Notification{
		Type:    model.EventRideCompleted,
		Payload: json.RawMessage(`{"RideId":"r1"}`),
	})

	want := "event: ride_completed\ndata: {\"RideId\":\"r1\"}\n\n"
	waitBody(t, rec, want, cancel, done)
	cancel()
	<-done
}

func TestStreamExitsAndUnregistersOnDisconnect(t *testing.T) {
	m := model.NewUsersMapper()
	h := NewStreamerHandler(m)

	ctx, cancel := context.WithCancel(context.Background())
	req := httptest.NewRequest(http.MethodGet, "/stream", nil).WithContext(ctx)
	req.Header.Set("X-User-Id", "u1")
	rec := newSyncRecorder()

	done := make(chan struct{})
	go func() { h.ServeHTTP(rec, req); close(done) }()

	waitRegistered(t, m, "u1")
	cancel()

	select {
	case <-done:
	case <-time.After(2 * time.Second):
		t.Fatal("handler did not return after client disconnect")
	}

	m.Mutex.RLock()
	_, stillThere := m.UsersMap["u1"]
	m.Mutex.RUnlock()
	if stillThere {
		t.Error("user not unregistered after disconnect")
	}
}
