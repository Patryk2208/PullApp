package main

import (
	"context"
	"errors"
	"log"
	"net/http"
	"os"
	"os/signal"
	"strings"
	"sync"
	"syscall"
	"time"

	firebase "firebase.google.com/go/v4"
	"google.golang.org/api/option"

	"notifications/internal/config"
	"notifications/internal/handler"
	"notifications/internal/health"
	"notifications/internal/kafka"
	"notifications/internal/model"
	"notifications/internal/postgres"
	"notifications/internal/service"
)

// noopPusher is used when FCM is not configured: SSE still delivers events,
// push is a no-op so the service runs without Firebase credentials.
type noopPusher struct{}

func (noopPusher) Notify(context.Context, string, model.Envelope) error { return nil }

func main() {
	cfg := config.Load()

	ctx, stop := signal.NotifyContext(context.Background(), syscall.SIGINT, syscall.SIGTERM)
	defer stop()

	db, err := postgres.NewClient(ctx, cfg.PostgresURL)
	if err != nil {
		log.Fatalf("postgres: %v", err)
	}
	defer db.Close()

	if err = postgres.Migrate(ctx, db); err != nil {
		log.Fatalf("migrate: %v", err)
	}

	repo := postgres.NewRepository(db)
	repo.StartCleanup(ctx, 24*time.Hour, 3) // run at 03:00 UTC, keep 24 h

	mapper := model.NewUsersMapper()
	streamer := service.NewStreamer(mapper)

	// Push (FCM) is optional. Without a Firebase project ID + usable credentials
	// (the local cluster ships PLACEHOLDER secrets) we run SSE-only instead of
	// crashing the whole service.
	var pusher service.Pusher = noopPusher{}
	if cfg.FirebaseProjectID == "" {
		log.Printf("FIREBASE_PROJECT_ID not set; push disabled, running SSE-only")
	} else if _, statErr := os.Stat(cfg.FirebaseCredentialsFile); statErr != nil {
		log.Printf("firebase credentials %q unavailable (%v); push disabled, running SSE-only", cfg.FirebaseCredentialsFile, statErr)
	} else {
		app, appErr := firebase.NewApp(ctx, &firebase.Config{ProjectID: cfg.FirebaseProjectID}, option.WithCredentialsFile(cfg.FirebaseCredentialsFile))
		if appErr != nil {
			log.Fatalf("firebase: %v", appErr)
		}
		fcm, msgErr := app.Messaging(ctx)
		if msgErr != nil {
			log.Fatalf("firebase messaging: %v", msgErr)
		}
		pusher = service.NewNotifier(repo, fcm)
	}

	dispatcher := service.NewDispatcher(repo, streamer, pusher)

	mux := http.NewServeMux()
	mux.Handle("/health", health.NewHandler(db, strings.SplitN(cfg.KafkaBrokers, ",", 2)[0]))
	mux.Handle("/devices/register", handler.NewRegisterHandler(repo))
	mux.Handle("/stream", handler.NewStreamerHandler(mapper))
	srv := &http.Server{Addr: cfg.HTTPAddr, Handler: mux}
	go func() {
		log.Printf("notifications: http listening on %s", cfg.HTTPAddr)
		if err = srv.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			log.Fatalf("http server: %v", err)
		}
	}()
	defer func() {
		if err = srv.Shutdown(context.Background()); err != nil {
			log.Printf("http server shutdown: %v", err)
		}
	}()

	// one consumer group per topic, each in its own goroutine
	topics := []string{
		model.TopicRideCompletions,
		model.TopicUserActions,
		model.TopicNotificationTriggers,
	}
	var wg sync.WaitGroup
	for _, topic := range topics {
		groupID := cfg.KafkaGroupID + "-" + topic
		consumer := kafka.NewConsumer(cfg.KafkaBrokers, topic, groupID, dispatcher)
		wg.Go(func() {
			log.Printf("notifications: consuming topic %s (group %s)", topic, groupID)
			if err = consumer.Run(ctx); err != nil {
				log.Printf("consumer %s: %v", topic, err)
			}
		})
	}

	<-ctx.Done()
	wg.Wait()
}
