package redis

import (
	"context"
	"time"
)

const positionTTL = 30 * time.Second

type Repository interface {
	SetPosition(ctx context.Context, routeID string, payload string) error
	GetPosition(ctx context.Context, routeID string) (string, error)
}

type redisRepository struct {
	client *Client
}

func NewRepository(client *Client) Repository {
	return &redisRepository{client: client}
}

func (r *redisRepository) SetPosition(ctx context.Context, routeID string, payload string) error {
	return r.client.rdb.Set(ctx, "position:"+routeID, payload, positionTTL).Err()
}

func (r *redisRepository) GetPosition(ctx context.Context, routeID string) (string, error) {
	return r.client.rdb.Get(ctx, "position:"+routeID).Result()
}
