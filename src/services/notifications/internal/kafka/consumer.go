package kafka

import (
	"context"
	"encoding/json"
	"log"
	"strings"

	kafka "github.com/segmentio/kafka-go"

	"notifications/internal/model"
	"notifications/internal/service"
)

type Consumer struct {
	reader     *kafka.Reader
	dispatcher *service.Dispatcher
}

func NewConsumer(brokers, topic, groupID string, dispatcher *service.Dispatcher) *Consumer {
	r := kafka.NewReader(kafka.ReaderConfig{
		Brokers: strings.Split(brokers, ","),
		Topic:   topic,
		GroupID: groupID,
		// notifications are real-time; start at the tail, no catch-up
		StartOffset: kafka.LastOffset,
	})
	return &Consumer{reader: r, dispatcher: dispatcher}
}

func (c *Consumer) Run(ctx context.Context) error {
	defer c.reader.Close()
	for {
		// manual commit: only after a successful dispatch
		msg, err := c.reader.FetchMessage(ctx)
		if err != nil {
			if ctx.Err() != nil {
				return nil
			}
			return err
		}

		var envelope model.Envelope
		if err = json.Unmarshal(msg.Value, &envelope); err != nil {
			log.Printf("kafka: unmarshal: %v", err)
			_ = c.reader.CommitMessages(ctx, msg)
			continue
		}

		if err = c.dispatcher.Dispatch(ctx, envelope); err != nil {
			log.Printf("kafka: dispatch event %s (%s): %v", envelope.EventId, envelope.EventType, err)
		}

		_ = c.reader.CommitMessages(ctx, msg)
	}
}
