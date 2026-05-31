//go:build integration

package redis

import (
	"context"
	"encoding/json"
	"testing"
	"time"

	tcredis "github.com/testcontainers/testcontainers-go/modules/redis"

	"notifications/internal/model"
)

func startRedis(t *testing.T) (*Client, func()) {
	t.Helper()
	ctx := context.Background()

	t.Setenv("TESTCONTAINERS_RYUK_DISABLED", "true")

	ctr, err := tcredis.Run(ctx, "redis:8-alpine")
	if err != nil {
		t.Fatalf("start redis container: %v", err)
	}

	addr, err := ctr.Endpoint(ctx, "")
	if err != nil {
		t.Fatalf("redis endpoint: %v", err)
	}

	client := New(addr, "")
	return client, func() {
		client.Close()
		_ = ctr.Terminate(ctx)
	}
}

func TestClaimEvent(t *testing.T) {
	client, cleanup := startRedis(t)
	defer cleanup()

	ctx := context.Background()

	claimed, err := client.ClaimEvent(ctx, "e1")
	if err != nil {
		t.Fatalf("ClaimEvent: %v", err)
	}
	if !claimed {
		t.Fatal("first claim should succeed")
	}

	claimed, err = client.ClaimEvent(ctx, "e1")
	if err != nil {
		t.Fatalf("ClaimEvent second: %v", err)
	}
	if claimed {
		t.Fatal("second claim of same event should return false")
	}

	// different event can be claimed
	claimed, _ = client.ClaimEvent(ctx, "e2")
	if !claimed {
		t.Fatal("different event should be claimable")
	}
}

func TestPublishSubscribe(t *testing.T) {
	client, cleanup := startRedis(t)
	defer cleanup()

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	env := model.Envelope{
		EventId:   "e1",
		EventType: model.EventRideRequested,
		Payload:   json.RawMessage(`{"passengerId":"P","driverId":"D"}`),
	}

	received := make(chan model.Envelope, 1)
	client.Subscribe(ctx, func(userID string, got model.Envelope) {
		if userID == "user-1" {
			received <- got
		}
	})

	// small delay to let PSUBSCRIBE land
	time.Sleep(50 * time.Millisecond)

	if err := client.Publish(ctx, "user-1", env); err != nil {
		t.Fatalf("Publish: %v", err)
	}

	select {
	case got := <-received:
		if got.EventId != env.EventId {
			t.Errorf("EventId = %q, want %q", got.EventId, env.EventId)
		}
	case <-time.After(3 * time.Second):
		t.Fatal("timed out waiting for message")
	}
}
