package redis

import (
	"context"
	"time"

	"github.com/redis/go-redis/v9"
)

type Client struct {
	rdb *redis.Client
}

func NewClient(addr, password string) *Client {
	return &Client{rdb: redis.NewClient(&redis.Options{Addr: addr, Password: password})}
}

func (c *Client) Start(ctx context.Context) error {
	return c.rdb.Ping(ctx).Err()
}

func (c *Client) TTL(ctx context.Context, key string) time.Duration {
	return c.rdb.TTL(ctx, key).Val()
}