package kafka

import (
	"context"
	"encoding/json"
	"log"
	"strings"
	"time"

	kafka "github.com/segmentio/kafka-go"

	"notifications/internal/metrics"
	"notifications/internal/model"
	"notifications/internal/service"
)

type Consumer struct {
	reader     *kafka.Reader
	dispatcher *service.Dispatcher
	metrics    *metrics.Metrics
}

func NewConsumer(brokers, topic, groupID string, dispatcher *service.Dispatcher, m *metrics.Metrics) *Consumer {
	r := kafka.NewReader(kafka.ReaderConfig{
		Brokers: strings.Split(brokers, ","),
		Topic:   topic,
		GroupID: groupID,
		// notifications are real-time; start at the tail, no catch-up
		StartOffset: kafka.LastOffset,
	})
	m.RegisterReader(r)
	return &Consumer{reader: r, dispatcher: dispatcher, metrics: m}
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

		start := time.Now()

		var envelope model.Envelope
		if err = json.Unmarshal(msg.Value, &envelope); err != nil {
			log.Printf("kafka: unmarshal: %v", err)
			_ = c.reader.CommitMessages(ctx, msg)
			continue
		}

		dispatchErr := c.dispatcher.Dispatch(ctx, envelope)
		status := "success"
		if dispatchErr != nil {
			status = "failed"
			log.Printf("kafka: dispatch event %s (%s): %v", envelope.EventId, envelope.EventType, dispatchErr)
		}

		c.metrics.RecordSent(ctx, "sse", status, envelope.EventType)
		c.metrics.RecordDuration(ctx, "sse", start)

		_ = c.reader.CommitMessages(ctx, msg)
	}
}
