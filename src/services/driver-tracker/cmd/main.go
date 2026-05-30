package main

import (
	"context"
	"log"
	"time"

	"github.com/gin-gonic/gin"

	"driver-tracker/internal/config"
	"driver-tracker/internal/handler"
	"driver-tracker/internal/health"
	redisrepo "driver-tracker/internal/redis"
	"driver-tracker/internal/service"
)

func main() {
	cfg := config.Load()

	rdb := redisrepo.NewClient(cfg.RedisAddr, cfg.RedisPassword)
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	if err := rdb.Start(ctx); err != nil {
		log.Fatalf("redis: %v", err)
	}

	repo := redisrepo.NewRepository(rdb)
	svc := service.NewTrackerService(repo)

	positionHandler := handler.NewPostPositionHandler(svc)
	trackRouteHandler := handler.NewTrackRouteHandler(svc)
	// todo: trackNearbyHandler := handler.NewTrackNearbyHandler(svc)
	healthHandler := health.NewHandler(rdb)

	r := gin.New()
	r.Use(gin.Recovery())

	r.POST("/position/:routeId", positionHandler.Handle)
	r.GET("/track/:routeId", trackRouteHandler.Handle)
	// todo: r.GET("/track", trackNearbyHandler.Handle)
	r.GET("/health", healthHandler.Handle)

	log.Printf("driver-tracker listening on %s", cfg.HTTPAddr)
	if err := r.Run(cfg.HTTPAddr); err != nil {
		log.Fatal(err)
	}
}
