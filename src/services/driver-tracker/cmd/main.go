package main

import (
	"context"
	"log"
	"time"

	"driver-tracker/internal/config"
	"driver-tracker/internal/handler"
	"driver-tracker/internal/health"
	redisrepo "driver-tracker/internal/redis"
	"driver-tracker/internal/service"

	"github.com/gin-gonic/gin"
)

func main() {
	cfg := config.Load()

	rdb := redisrepo.NewClient(cfg.RedisAddr)
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	if err := rdb.Start(ctx); err != nil {
		log.Fatalf("redis: %v", err)
	}

	repo := redisrepo.NewRepository(rdb)
	updateService := service.NewTrackerService(repo)
	trackerService := service.NewTrackerService(repo)

	positionHandler := handler.NewPostPositionHandler(updateService)
	trackHandler := handler.NewTrackRouteHandler(trackerService)
	healthHandler := health.NewHandler(rdb)

	r := gin.New()
	r.Use(gin.Recovery())

	r.POST("/position", positionHandler.Handle)
	r.GET("/track/:routeId", trackHandler.Handle)
	r.GET("/health", healthHandler.Handle)

	log.Printf("driver-tracker listening on %s", cfg.HTTPAddr)
	if err := r.Run(cfg.HTTPAddr); err != nil {
		log.Fatal(err)
	}
}
