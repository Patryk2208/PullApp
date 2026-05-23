package redis

import (
	"context"
	"encoding/json"
	"time"

	"github.com/google/uuid"

	"driver-tracker/internal/model"
)

const positionTTL = 30 * time.Second

type redisRepository struct {
	client *Client
}

func NewRepository(client *Client) *redisRepository {
	return &redisRepository{client: client}
}

func (r *redisRepository) SetRouteDriverPosition(ctx context.Context, routeId uuid.UUID, point model.GeoPoint) error {
	raw, err := json.Marshal(point)
	if err != nil {
		return err
	}
	return r.client.rdb.Set(ctx, "position:"+routeId.String(), raw, positionTTL).Err()
}

func (r *redisRepository) GetRouteDriverPosition(ctx context.Context, routeId uuid.UUID) (model.DriverPosition, error) {
	raw, err := r.client.rdb.Get(ctx, "position:"+routeId.String()).Result()
	if err != nil {
		return model.DriverPosition{}, err
	}

	var point model.GeoPoint
	if err := json.Unmarshal([]byte(raw), &point); err != nil {
		return model.DriverPosition{}, err
	}

	return model.DriverPosition{
		RouteID:  routeId.String(),
		GeoPoint: point,
	}, nil
}