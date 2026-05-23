package redis_test

import (
	"context"
	"errors"
	"testing"

	"github.com/google/uuid"
	"github.com/redis/go-redis/v9"
	tcredis "github.com/testcontainers/testcontainers-go/modules/redis"

	"driver-tracker/internal/model"
	redisrepo "driver-tracker/internal/redis"
)

func setupRedis(t *testing.T) (*redisrepo.Client, func()) {
	t.Helper()
	ctx := context.Background()

	t.Setenv("TESTCONTAINERS_RYUK_DISABLED", "true")
	container, err := tcredis.Run(ctx, "redis:8-alpine")
	if err != nil {
		t.Fatalf("start redis container: %v", err)
	}

	addr, err := container.Endpoint(ctx, "")
	if err != nil {
		t.Fatalf("redis endpoint: %v", err)
	}

	client := redisrepo.NewClient(addr, "")
	if err := client.Start(ctx); err != nil {
		t.Fatalf("redis ping: %v", err)
	}

	return client, func() { _ = container.Terminate(ctx) }
}

func TestSetAndGetRouteDriverPosition(t *testing.T) {
	client, cleanup := setupRedis(t)
	defer cleanup()

	repo := redisrepo.NewRepository(client)
	ctx := context.Background()
	routeId := uuid.New()
	point := model.GeoPoint{Lat: 52.2297, Lng: 21.0122}

	if err := repo.SetRouteDriverPosition(ctx, routeId, point); err != nil {
		t.Fatalf("SetRouteDriverPosition: %v", err)
	}

	got, err := repo.GetRouteDriverPosition(ctx, routeId)
	if err != nil {
		t.Fatalf("GetRouteDriverPosition: %v", err)
	}

	if got.GeoPoint.Lat != point.Lat || got.GeoPoint.Lng != point.Lng {
		t.Errorf("got %+v, want %+v", got.GeoPoint, point)
	}
	if got.RouteID != routeId.String() {
		t.Errorf("RouteID got %s, want %s", got.RouteID, routeId.String())
	}
}

func TestGetRouteDriverPosition_NotFound(t *testing.T) {
	client, cleanup := setupRedis(t)
	defer cleanup()

	repo := redisrepo.NewRepository(client)
	ctx := context.Background()

	_, err := repo.GetRouteDriverPosition(ctx, uuid.New())
	if err == nil {
		t.Fatal("expected error for missing key, got nil")
	}
	if !errors.Is(err, redis.Nil) {
		t.Errorf("expected redis.Nil, got %v", err)
	}
}

func TestSetRouteDriverPosition_TTL(t *testing.T) {
	client, cleanup := setupRedis(t)
	defer cleanup()

	repo := redisrepo.NewRepository(client)
	ctx := context.Background()
	routeId := uuid.New()

	if err := repo.SetRouteDriverPosition(ctx, routeId, model.GeoPoint{Lat: 1, Lng: 2}); err != nil {
		t.Fatalf("SetRouteDriverPosition: %v", err)
	}

	ttl := client.TTL(ctx, "position:"+routeId.String())
	if ttl <= 0 {
		t.Errorf("expected TTL > 0, got %v", ttl)
	}
}

func TestSetRouteDriverPosition_Overwrite(t *testing.T) {
	client, cleanup := setupRedis(t)
	defer cleanup()

	repo := redisrepo.NewRepository(client)
	ctx := context.Background()
	routeId := uuid.New()

	_ = repo.SetRouteDriverPosition(ctx, routeId, model.GeoPoint{Lat: 1.0, Lng: 2.0})
	updated := model.GeoPoint{Lat: 52.2297, Lng: 21.0122}
	_ = repo.SetRouteDriverPosition(ctx, routeId, updated)

	got, err := repo.GetRouteDriverPosition(ctx, routeId)
	if err != nil {
		t.Fatalf("GetRouteDriverPosition: %v", err)
	}
	if got.GeoPoint.Lat != updated.Lat || got.GeoPoint.Lng != updated.Lng {
		t.Errorf("got %+v, want %+v", got.GeoPoint, updated)
	}
}
