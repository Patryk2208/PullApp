//go:build integration

package kafka

import (
	"context"
	"encoding/json"
	"sync"
	"testing"
	"time"

	segkafka "github.com/segmentio/kafka-go"
	tckafka "github.com/testcontainers/testcontainers-go/modules/kafka"

	"notifications/internal/metrics"
	"notifications/internal/model"
	"notifications/internal/service"
)

// memIdem is an in-memory IdempotencyRepository for the pipeline test.
type memIdem struct {
	mu   sync.Mutex
	seen map[string]bool
}

func newMemIdem() *memIdem { return &memIdem{seen: map[string]bool{}} }

func (m *memIdem) ClaimEvent(_ context.Context, id string) (bool, error) {
	m.mu.Lock()
	defer m.mu.Unlock()
	if m.seen[id] {
		return false, nil
	}
	m.seen[id] = true
	return true, nil
}

// directPublisher bypasses Redis and delivers straight to the streamer —
// sufficient for the Kafka pipeline integration test.
type directPublisher struct{ s *service.Streamer }

func (p *directPublisher) Publish(_ context.Context, userID string, env model.Envelope) error {
	p.s.Send(userID, env)
	return nil
}

// noopPusher satisfies the Pusher interface without touching FCM.
type noopPusher struct{}

func (noopPusher) Notify(context.Context, string, model.Envelope) error { return nil }

func startKafka(t *testing.T) (string, func()) {
	t.Helper()
	ctx := context.Background()

	// ryuk (the resource reaper) cannot start in some local/CI sandboxes;
	// disable it and rely on explicit Terminate in cleanup.
	t.Setenv("TESTCONTAINERS_RYUK_DISABLED", "true")

	// confluent-local is the image the testcontainers kafka module supports out
	// of the box (KRaft + listeners pre-wired). The platform broker runs
	// apache/kafka:3.9.0 in compose; the wire protocol is identical, so this is
	// only a test-harness convenience.
	ctr, err := tckafka.Run(ctx, "confluentinc/confluent-local:7.6.0",
		tckafka.WithClusterID("test-cluster"),
	)
	if err != nil {
		t.Fatalf("start kafka container: %v", err)
	}

	brokers, err := ctr.Brokers(ctx)
	if err != nil {
		t.Fatalf("brokers: %v", err)
	}

	cleanup := func() { _ = ctr.Terminate(ctx) }
	return brokers[0], cleanup
}

func createTopic(t *testing.T, broker, topic string) {
	t.Helper()
	conn, err := segkafka.Dial("tcp", broker)
	if err != nil {
		t.Fatalf("dial: %v", err)
	}
	defer conn.Close()

	if err := conn.CreateTopics(segkafka.TopicConfig{
		Topic:             topic,
		NumPartitions:     1,
		ReplicationFactor: 1,
	}); err != nil {
		t.Fatalf("create topic: %v", err)
	}
}

// TestConsumerPipeline exercises the full inbound path:
// produce an Envelope to Kafka → Consumer → Dispatcher → UsersMapper channel.
func TestConsumerPipeline(t *testing.T) {
	broker, cleanup := startKafka(t)
	defer cleanup()

	topic := model.TopicUserActions
	createTopic(t, broker, topic)

	// wire a real dispatcher over an in-memory idempotency store + real streamer
	mapper := model.NewUsersMapper()
	streamer := service.NewStreamer(mapper)
	dispatcher := service.NewDispatcher(newMemIdem(), &directPublisher{streamer}, noopPusher{})

	ch := mapper.Register("driver-1")
	defer mapper.Unregister("driver-1", ch)

	m, _ := metrics.New()
	consumer := NewConsumer(broker, topic, "notifications-test", dispatcher, m)
	ctx, stop := context.WithCancel(context.Background())
	defer stop()
	go func() { _ = consumer.Run(ctx) }()

	// produce a route_selected event addressed to driver-1
	payload, _ := json.Marshal(model.RouteSelectedPayload{
		RequestId:   "req-1",
		DriverId:    "driver-1",
		PassengerId: "passenger-1",
	})
	env := model.Envelope{
		EventId:    "evt-1",
		EventType:  model.EventRouteSelected,
		OccurredAt: time.Now(),
		Payload:    payload,
	}
	raw, _ := json.Marshal(env)

	w := &segkafka.Writer{
		Addr:                   segkafka.TCP(broker),
		Topic:                  topic,
		AllowAutoTopicCreation: true,
	}
	defer w.Close()

	// The consumer starts at the tail (StartOffset: LastOffset), so a single
	// produce can land before the group has joined and be missed. Keep producing
	// the same event until it is received — the dispatcher dedupes on EventId, so
	// only the first copy that is actually read gets delivered to the channel.
	produce := make(chan struct{})
	var producer sync.WaitGroup
	producer.Add(1)
	go func() {
		defer producer.Done()
		ticker := time.NewTicker(time.Second)
		defer ticker.Stop()
		for {
			if err := w.WriteMessages(ctx, segkafka.Message{Value: raw}); err != nil && ctx.Err() == nil {
				t.Logf("produce (will retry): %v", err)
			}
			select {
			case <-produce:
				return
			case <-ticker.C:
			}
		}
	}()

	var got model.Notification
	var ok bool
	select {
	case got, ok = <-ch:
	case <-time.After(60 * time.Second):
		t.Error("event never reached the user channel through the pipeline")
	}

	// stop producing and wait for the goroutine before the test returns, so it
	// can't touch t after the test completes.
	close(produce)
	producer.Wait()

	if ok {
		if got.Type != model.EventRouteSelected {
			t.Errorf("Type = %q, want %q", got.Type, model.EventRouteSelected)
		}
		if string(got.Payload) != string(payload) {
			t.Errorf("Payload = %s, want %s", got.Payload, payload)
		}
	}
}