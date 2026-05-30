package metrics

import (
	"context"
	"sync"
	"time"

	kafka "github.com/segmentio/kafka-go"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/metric"
)

const meterName = "notifications"

type Metrics struct {
	Sent     metric.Int64Counter
	Duration metric.Float64Histogram

	mu      sync.Mutex
	readers []*kafka.Reader
}

func New() (*Metrics, error) {
	meter := otel.Meter(meterName)

	sent, err := meter.Int64Counter(
		"notifications_sent_total",
		metric.WithDescription("Number of notifications sent per channel, status, and event type"),
		metric.WithUnit("notifications"),
	)
	if err != nil {
		return nil, err
	}

	duration, err := meter.Float64Histogram(
		"notification_delivery_duration_seconds",
		metric.WithDescription("Time from Kafka message receipt to delivery via provider"),
		metric.WithUnit("s"),
	)
	if err != nil {
		return nil, err
	}

	m := &Metrics{Sent: sent, Duration: duration}

	_, err = meter.Int64ObservableGauge(
		"notification_kafka_lag",
		metric.WithDescription("How far behind the Kafka consumers are"),
		metric.WithUnit("messages"),
		metric.WithInt64Callback(func(_ context.Context, o metric.Int64Observer) error {
			m.mu.Lock()
			defer m.mu.Unlock()
			for _, r := range m.readers {
				stats := r.Stats()
				o.Observe(stats.Lag, metric.WithAttributes())
			}
			return nil
		}),
	)
	if err != nil {
		return nil, err
	}

	return m, nil
}

// RegisterReader registers a kafka reader so its lag is included in the observable gauge.
func (m *Metrics) RegisterReader(r *kafka.Reader) {
	m.mu.Lock()
	m.readers = append(m.readers, r)
	m.mu.Unlock()
}

// RecordSent increments notifications_sent_total.
func (m *Metrics) RecordSent(ctx context.Context, channel, status, eventType string) {
	m.Sent.Add(ctx, 1,
		metric.WithAttributes(
			channelAttr(channel),
			statusAttr(status),
			eventTypeAttr(eventType),
		),
	)
}

// RecordDuration records notification_delivery_duration_seconds.
func (m *Metrics) RecordDuration(ctx context.Context, channel string, start time.Time) {
	m.Duration.Record(ctx, time.Since(start).Seconds(),
		metric.WithAttributes(channelAttr(channel)),
	)
}
