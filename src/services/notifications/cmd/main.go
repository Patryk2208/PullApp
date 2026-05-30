package main

import (
	"context"
	"errors"
	"log"
	"net/http"
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

func main() {
	cfg := config.Load()

	ctx, stop := signal.NotifyContext(context.Background(), syscall.SIGINT, syscall.SIGTERM)
	defer stop()

	db, err := postgres.NewClient(ctx, cfg.PostgresURL)
	if err != nil {
		log.Fatalf("postgres: %v", err)
	}
	defer db.Close()

	app, err := firebase.NewApp(ctx, &firebase.Config{ProjectID: cfg.FirebaseProjectID}, option.WithCredentialsFile(cfg.FirebaseCredentialsFile))
	if err != nil {
		log.Fatalf("firebase: %v", err)
	}
	fcm, err := app.Messaging(ctx)
	if err != nil {
		log.Fatalf("firebase messaging: %v", err)
	}

	repo := postgres.NewRepository(db)
	repo.StartCleanup(ctx, 24*time.Hour, 3) // run at 03:00 UTC, keep 24 h

	mapper := model.NewUsersMapper()
	streamer := service.NewStreamer(mapper)
	notifier := service.NewNotifier(repo, fcm)
	dispatcher := service.NewDispatcher(repo, streamer, notifier)

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
