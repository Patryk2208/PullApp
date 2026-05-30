package redis

import (
	"context"
	"encoding/json"
	"fmt"
	"log"
	"time"

	"github.com/redis/go-redis/v9"

	"notifications/internal/model"
)

const (
	channelPrefix  = "notifications:"
	idempotencyTTL = 24 * time.Hour
)

type Client struct {
	rdb *redis.Client
}

func New(addr, password string) *Client {
	return &Client{rdb: redis.NewClient(&redis.Options{
		Addr:     addr,
		Password: password,
	})}
}

func (c *Client) Close() error {
	return c.rdb.Close()
}

// ClaimEvent atomically claims the event using SET NX. Returns true if this
// caller is the first to process it.
func (c *Client) ClaimEvent(ctx context.Context, eventID string) (bool, error) {
	ok, err := c.rdb.SetNX(ctx, "idem:"+eventID, 1, idempotencyTTL).Result()
	return ok, err
}

// Publish serialises env and publishes it to the per-user channel.
func (c *Client) Publish(ctx context.Context, userID string, env model.Envelope) error {
	b, err := json.Marshal(env)
	if err != nil {
		return fmt.Errorf("marshal envelope: %w", err)
	}
	return c.rdb.Publish(ctx, channelPrefix+userID, b).Err()
}

// Subscribe starts a PSUBSCRIBE goroutine. For every message on any
// notifications:* channel it calls deliver(userID, envelope). Runs until ctx
// is cancelled.
func (c *Client) Subscribe(ctx context.Context, deliver func(userID string, env model.Envelope)) {
	psub := c.rdb.PSubscribe(ctx, channelPrefix+"*")
	go func() {
		defer psub.Close()
		ch := psub.Channel()
		for {
			select {
			case msg, ok := <-ch:
				if !ok {
					return
				}
				userID := msg.Channel[len(channelPrefix):]
				var env model.Envelope
				if err := json.Unmarshal([]byte(msg.Payload), &env); err != nil {
					log.Printf("redis subscriber: unmarshal: %v", err)
					continue
				}
				deliver(userID, env)
			case <-ctx.Done():
				return
			}
		}
	}()
}