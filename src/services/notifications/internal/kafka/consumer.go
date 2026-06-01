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
	topic := c.reader.Config().Topic
	defer c.reader.Close()
	for {
		msg, err := c.reader.FetchMessage(ctx)
		if err != nil {
			if ctx.Err() != nil {
				return nil
			}
			return err
		}

		log.Printf("kafka[%s]: received message partition=%d offset=%d len=%d",
			topic, msg.Partition, msg.Offset, len(msg.Value))

		start := time.Now()

		var envelope model.Envelope
		if err = json.Unmarshal(msg.Value, &envelope); err != nil {
			log.Printf("kafka[%s]: unmarshal error offset=%d: %v — raw: %.200s",
				topic, msg.Offset, err, msg.Value)
			_ = c.reader.CommitMessages(ctx, msg)
			continue
		}

		log.Printf("kafka[%s]: dispatching eventId=%s type=%s", topic, envelope.EventId, envelope.EventType)

		dispatchErr := c.dispatcher.Dispatch(ctx, envelope)
		status := "success"
		if dispatchErr != nil {
			status = "failed"
			log.Printf("kafka[%s]: dispatch failed eventId=%s type=%s: %v",
				topic, envelope.EventId, envelope.EventType, dispatchErr)
		} else {
			log.Printf("kafka[%s]: dispatch ok eventId=%s type=%s duration=%s",
				topic, envelope.EventId, envelope.EventType, time.Since(start))
		}

		c.metrics.RecordSent(ctx, "sse", status, envelope.EventType)
		c.metrics.RecordDuration(ctx, "sse", start)

		_ = c.reader.CommitMessages(ctx, msg)
	}
}
