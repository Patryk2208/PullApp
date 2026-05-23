package main

import (
	"context"
	"errors"
	"log"
	"net/http"
	"os/signal"
	"strings"
	"syscall"
	"time"

	firebase "firebase.google.com/go/v4"
	"google.golang.org/api/option"

	"notifications/internal/config"
	"notifications/internal/handler"
	"notifications/internal/health"
	"notifications/internal/kafka"
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

	svc := service.NewNotifier(repo, fcm)

	mux := http.NewServeMux()
	mux.Handle("/health", health.NewHandler(db, strings.SplitN(cfg.KafkaBrokers, ",", 2)[0]))
	mux.Handle("/devices/register", handler.NewRegisterHandler(repo))
	srv := &http.Server{Addr: cfg.HTTPAddr, Handler: mux}
	go func() {
		log.Printf("notifications: health listening on %s", cfg.HTTPAddr)
		if err := srv.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			log.Fatalf("health server: %v", err)
		}
	}()
	defer func() {
		if err := srv.Shutdown(context.Background()); err != nil {
			log.Printf("health server shutdown: %v", err)
		}
	}()

	consumer := kafka.NewConsumer(cfg.KafkaBrokers, cfg.KafkaTopic, cfg.KafkaGroupID, svc)
	log.Printf("notifications: consuming topic %s (group %s)", cfg.KafkaTopic, cfg.KafkaGroupID)
	if err := consumer.Run(ctx); err != nil {
		log.Fatalf("consumer: %v", err)
	}
}
