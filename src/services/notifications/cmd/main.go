package main

import (
	"context"
	"log"
	"os/signal"
	"syscall"
	"time"

	"notifications/internal/config"
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

	repo := postgres.NewRepository(db)
	repo.StartCleanup(ctx, 24*time.Hour, 3) // run at 03:00 UTC, keep 24 h

	svc := service.NewNotifier(repo)

	consumer := kafka.NewConsumer(cfg.KafkaBrokers, cfg.KafkaTopic, cfg.KafkaGroupID, svc)
	log.Printf("notifications: consuming topic %s (group %s)", cfg.KafkaTopic, cfg.KafkaGroupID)
	if err := consumer.Run(ctx); err != nil {
		log.Fatalf("consumer: %v", err)
	}
}