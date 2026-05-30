package postgres

import (
	"context"
	"log"
	"time"

	"github.com/jackc/pgx/v5/pgxpool"
)

type Repository struct {
	db *pgxpool.Pool
}

func NewRepository(db *pgxpool.Pool) *Repository {
	return &Repository{db: db}
}

func (r *Repository) GetDeviceToken(ctx context.Context, userID string) (string, error) {
	var token string
	err := r.db.QueryRow(ctx, "SELECT token FROM device_tokens WHERE user_id = $1", userID).Scan(&token)
	return token, err
}

func (r *Repository) IsProcessed(ctx context.Context, eventID string) (bool, error) {
	var exists bool
	err := r.db.QueryRow(ctx, "SELECT EXISTS(SELECT 1 FROM sent_notifications WHERE event_id = $1)", eventID).Scan(&exists)
	return exists, err
}

func (r *Repository) MarkProcessed(ctx context.Context, eventID string) error {
	_, err := r.db.Exec(ctx, "INSERT INTO sent_notifications (event_id) VALUES ($1) ON CONFLICT DO NOTHING", eventID)
	return err
}

func (r *Repository) DeleteDeviceToken(ctx context.Context, userID string) error {
	_, err := r.db.Exec(ctx, "DELETE FROM device_tokens WHERE user_id = $1", userID)
	return err
}

func (r *Repository) UpsertDeviceToken(ctx context.Context, userID, token, platform string) error {
	_, err := r.db.Exec(ctx, `
		INSERT INTO device_tokens (user_id, token, platform, updated_at)
		VALUES ($1, $2, $3, now())
		ON CONFLICT (user_id) DO UPDATE
			SET token      = EXCLUDED.token,
			    platform   = EXCLUDED.platform,
			    updated_at = now()`,
		userID, token, platform)
	return err
}

func (r *Repository) DeleteOldNotifications(ctx context.Context, olderThan time.Duration) error {
	// make_interval takes seconds as a float; Go's Duration.String() ("24h0m0s")
	// is not valid Postgres interval syntax.
	_, err := r.db.Exec(ctx, "DELETE FROM sent_notifications WHERE sent_at < now() - make_interval(secs => $1)", olderThan.Seconds())
	return err
}

// StartCleanup runs DeleteOldNotifications immediately, then every day at cleanupHour (UTC).
func (r *Repository) StartCleanup(ctx context.Context, olderThan time.Duration, cleanupHour int) {
	run := func() {
		if err := r.DeleteOldNotifications(ctx, olderThan); err != nil {
			log.Printf("cleanup: %v", err)
		} else {
			log.Printf("cleanup: old sent_notifications deleted")
		}
	}

	go func() {
		run()
		for {
			now := time.Now().UTC()
			next := time.Date(now.Year(), now.Month(), now.Day(), cleanupHour, 0, 0, 0, time.UTC)
			if !next.After(now) {
				next = next.Add(24 * time.Hour)
			}
			select {
			case <-time.After(time.Until(next)):
				run()
			case <-ctx.Done():
				return
			}
		}
	}()
}