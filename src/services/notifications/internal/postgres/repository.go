package postgres

import (
	"context"

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
