//go:build integration

package postgres

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"
	"github.com/testcontainers/testcontainers-go"
	tcpostgres "github.com/testcontainers/testcontainers-go/modules/postgres"
	"github.com/testcontainers/testcontainers-go/wait"
)


func startPostgres(t *testing.T) (*pgxpool.Pool, func()) {
	t.Helper()
	ctx := context.Background()

	// ryuk (the resource reaper) cannot start in some local/CI sandboxes;
	// disable it and rely on explicit Terminate in cleanup.
	t.Setenv("TESTCONTAINERS_RYUK_DISABLED", "true")

	ctr, err := tcpostgres.Run(ctx, "postgres:17-alpine",
		tcpostgres.WithDatabase("notifications"),
		tcpostgres.WithUsername("notifications"),
		tcpostgres.WithPassword("notifications"),
		testcontainers.WithWaitStrategy(
			wait.ForLog("database system is ready to accept connections").
				WithOccurrence(2).
				WithStartupTimeout(60*time.Second),
		),
	)
	if err != nil {
		t.Fatalf("start postgres container: %v", err)
	}

	dsn, err := ctr.ConnectionString(ctx, "sslmode=disable")
	if err != nil {
		t.Fatalf("connection string: %v", err)
	}

	pool, err := pgxpool.New(ctx, dsn)
	if err != nil {
		t.Fatalf("pgxpool: %v", err)
	}
	if err := Migrate(ctx, pool); err != nil {
		t.Fatalf("migrate: %v", err)
	}

	cleanup := func() {
		pool.Close()
		_ = ctr.Terminate(ctx)
	}
	return pool, cleanup
}

func TestDeviceTokenLifecycle(t *testing.T) {
	pool, cleanup := startPostgres(t)
	defer cleanup()

	ctx := context.Background()
	repo := NewRepository(pool)

	// missing token → ErrNoRows
	if _, err := repo.GetDeviceToken(ctx, "u1"); !errors.Is(err, pgx.ErrNoRows) {
		t.Fatalf("expected ErrNoRows for missing token, got %v", err)
	}

	// upsert then read back
	if err := repo.UpsertDeviceToken(ctx, "u1", "tok-1", "android"); err != nil {
		t.Fatalf("upsert: %v", err)
	}
	got, err := repo.GetDeviceToken(ctx, "u1")
	if err != nil {
		t.Fatalf("get: %v", err)
	}
	if got != "tok-1" {
		t.Errorf("token = %q, want tok-1", got)
	}

	// upsert again updates in place (conflict on user_id)
	if err := repo.UpsertDeviceToken(ctx, "u1", "tok-2", "ios"); err != nil {
		t.Fatalf("upsert update: %v", err)
	}
	got, _ = repo.GetDeviceToken(ctx, "u1")
	if got != "tok-2" {
		t.Errorf("token after update = %q, want tok-2", got)
	}

	// delete
	if err := repo.DeleteDeviceToken(ctx, "u1"); err != nil {
		t.Fatalf("delete: %v", err)
	}
	if _, err := repo.GetDeviceToken(ctx, "u1"); !errors.Is(err, pgx.ErrNoRows) {
		t.Fatalf("expected ErrNoRows after delete, got %v", err)
	}
}

func TestIdempotencyMarkAndCheck(t *testing.T) {
	pool, cleanup := startPostgres(t)
	defer cleanup()

	ctx := context.Background()
	repo := NewRepository(pool)

	done, err := repo.IsProcessed(ctx, "e1")
	if err != nil {
		t.Fatalf("IsProcessed: %v", err)
	}
	if done {
		t.Fatal("event should not be processed yet")
	}

	if err := repo.MarkProcessed(ctx, "e1"); err != nil {
		t.Fatalf("MarkProcessed: %v", err)
	}

	done, _ = repo.IsProcessed(ctx, "e1")
	if !done {
		t.Fatal("event should be processed after MarkProcessed")
	}

	// marking the same event twice must not error (ON CONFLICT DO NOTHING)
	if err := repo.MarkProcessed(ctx, "e1"); err != nil {
		t.Fatalf("second MarkProcessed should be a no-op, got %v", err)
	}
}

func TestDeleteOldNotifications(t *testing.T) {
	pool, cleanup := startPostgres(t)
	defer cleanup()

	ctx := context.Background()
	repo := NewRepository(pool)

	// one fresh, one old
	if _, err := pool.Exec(ctx,
		"INSERT INTO sent_notifications (event_id, sent_at) VALUES ($1, now()), ($2, now() - interval '48 hours')",
		"fresh", "old"); err != nil {
		t.Fatalf("seed: %v", err)
	}

	if err := repo.DeleteOldNotifications(ctx, 24*time.Hour); err != nil {
		t.Fatalf("DeleteOldNotifications: %v", err)
	}

	freshThere, _ := repo.IsProcessed(ctx, "fresh")
	oldThere, _ := repo.IsProcessed(ctx, "old")
	if !freshThere {
		t.Error("fresh notification was wrongly deleted")
	}
	if oldThere {
		t.Error("old notification should have been deleted")
	}
}
