package service

import (
	"context"
	"errors"
	"sync"
	"testing"
	"time"

	"github.com/google/uuid"

	"driver-tracker/internal/model"
)

// --- mock ---

type mockRepo struct {
	mu     sync.Mutex
	getErr error
	setErr error
	stored model.GeoPoint
}

func (m *mockRepo) SetRouteDriverPosition(_ context.Context, _ uuid.UUID, point model.GeoPoint) error {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.stored = point
	return m.setErr
}

func (m *mockRepo) GetRouteDriverPosition(_ context.Context, id uuid.UUID) (model.DriverPosition, error) {
	m.mu.Lock()
	defer m.mu.Unlock()
	if m.getErr != nil {
		return model.DriverPosition{}, m.getErr
	}
	return model.DriverPosition{RouteID: id.String(), GeoPoint: m.stored}, nil
}

// --- UpdateDriverPosition ---

func TestUpdateDriverPosition_OK(t *testing.T) {
	repo := &mockRepo{}
	svc := NewTrackerService(repo)

	point := model.GeoPoint{Lat: 52.2, Lng: 21.0}
	if err := svc.UpdateDriverPosition(context.Background(), uuid.New(), point); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	repo.mu.Lock()
	stored := repo.stored
	repo.mu.Unlock()
	if stored != point {
		t.Errorf("stored = %+v, want %+v", stored, point)
	}
}

func TestUpdateDriverPosition_RepoError(t *testing.T) {
	repo := &mockRepo{setErr: errors.New("redis down")}
	svc := NewTrackerService(repo)

	if err := svc.UpdateDriverPosition(context.Background(), uuid.New(), model.GeoPoint{}); err == nil {
		t.Fatal("expected error, got nil")
	}
}

// --- Run ---

func TestRun_DeliversPosition(t *testing.T) {
	point := model.GeoPoint{Lat: 52.2297, Lng: 21.0122}
	repo := &mockRepo{stored: point}
	svc := NewTrackerService(repo)

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	go svc.Run(ctx)

	routeId := uuid.New()
	svc.GetInputChannel() <- model.RoutePositionRequest{RouteId: routeId}

	select {
	case pos := <-svc.GetOutputChannel():
		if pos.RouteID != routeId.String() {
			t.Errorf("RouteID = %s, want %s", pos.RouteID, routeId.String())
		}
		if pos.GeoPoint != point {
			t.Errorf("GeoPoint = %+v, want %+v", pos.GeoPoint, point)
		}
	case <-time.After(2 * time.Second):
		t.Fatal("no position delivered to output channel")
	}
}

func TestRun_MultipleRequests(t *testing.T) {
	repo := &mockRepo{}
	svc := NewTrackerService(repo)

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	go svc.Run(ctx)

	const n = 5
	ids := make([]uuid.UUID, n)
	for i := range n {
		ids[i] = uuid.New()
	}

	// set a single stored point; repo returns it for any routeId
	repo.mu.Lock()
	repo.stored = model.GeoPoint{Lat: float64(1), Lng: float64(2)}
	repo.mu.Unlock()

	for _, id := range ids {
		svc.GetInputChannel() <- model.RoutePositionRequest{RouteId: id}
	}

	received := 0
	timeout := time.After(5 * time.Second)
	for received < n {
		select {
		case <-svc.GetOutputChannel():
			received++
		case <-timeout:
			t.Fatalf("only got %d/%d positions", received, n)
		}
	}
}

func TestRun_RepoErrorContinues(t *testing.T) {
	repo := &mockRepo{getErr: errors.New("redis down")}
	svc := NewTrackerService(repo)

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	go svc.Run(ctx)

	// send a request while repo errors — nothing should arrive and goroutine must not crash
	svc.GetInputChannel() <- model.RoutePositionRequest{RouteId: uuid.New()}

	// give the goroutine time to process
	time.Sleep(50 * time.Millisecond)

	// clear the error; next request should succeed
	repo.mu.Lock()
	repo.getErr = nil
	repo.stored = model.GeoPoint{Lat: 10, Lng: 20}
	repo.mu.Unlock()

	routeId := uuid.New()
	svc.GetInputChannel() <- model.RoutePositionRequest{RouteId: routeId}

	select {
	case pos := <-svc.GetOutputChannel():
		if pos.GeoPoint.Lat != 10 {
			t.Errorf("got %+v", pos)
		}
	case <-time.After(2 * time.Second):
		t.Fatal("Run did not continue after repo error")
	}
}

func TestRun_ContextCancel(t *testing.T) {
	repo := &mockRepo{}
	svc := NewTrackerService(repo)

	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan struct{})
	go func() {
		svc.Run(ctx)
		close(done)
	}()

	cancel()
	select {
	case <-done:
	case <-time.After(time.Second):
		t.Fatal("Run did not exit after context cancellation")
	}
}

func TestTrackerService_ChannelsAreBuffered(t *testing.T) {
	repo := &mockRepo{}
	svc := NewTrackerService(repo)

	// buffered channels must not block on a single send without a receiver
	done := make(chan struct{})
	go func() {
		svc.GetInputChannel() <- model.RoutePositionRequest{}
		close(done)
	}()
	select {
	case <-done:
	case <-time.After(time.Second):
		t.Fatal("input channel blocked — expected buffered")
	}
}
