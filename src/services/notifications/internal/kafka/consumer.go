package kafka

import (
	"context"
	"encoding/json"
	"log"
	"strings"

	kafka "github.com/segmentio/kafka-go"

	"notifications/internal/service"
)

type Consumer struct {
	reader *kafka.Reader
	svc    *service.Notifier
}

func NewConsumer(brokers, topic, groupID string, svc *service.Notifier) *Consumer {
	r := kafka.NewReader(kafka.ReaderConfig{
		Brokers: strings.Split(brokers, ","),
		Topic:   topic,
		GroupID: groupID,
	})
	return &Consumer{reader: r, svc: svc}
}

func (c *Consumer) Run(ctx context.Context) error {
	defer c.reader.Close()
	for {
		msg, err := c.reader.FetchMessage(ctx)
		if err != nil {
			if ctx.Err() != nil {
				return nil
			}
			return err
		}

		var event service.Event
		if err := json.Unmarshal(msg.Value, &event); err != nil {
			log.Printf("kafka: unmarshal: %v", err)
			_ = c.reader.CommitMessages(ctx, msg)
			continue
		}

		if err := c.svc.Handle(ctx, event); err != nil {
			log.Printf("kafka: handle event %s (%s): %v", event.ID, event.Type, err)
		}

		_ = c.reader.CommitMessages(ctx, msg)
	}
}