package postgres

import (
	"context"

	"github.com/jackc/pgx/v5/pgxpool"
)

func NewClient(ctx context.Context, url string) (*pgxpool.Pool, error) {
	return pgxpool.New(ctx, url)
}