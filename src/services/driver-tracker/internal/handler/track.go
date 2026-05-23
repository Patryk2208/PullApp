package handler

import (
	"context"
	"net/http"

	"github.com/gin-gonic/gin"
	"github.com/gorilla/websocket"
)

var upgrader = websocket.Upgrader{
	CheckOrigin: func(r *http.Request) bool { return true },
}

type PositionTrackingService interface {
	GetAllDriversNearby(ctx context.Context) error
}

type TrackRouteHandler struct {
	svc PositionTrackingService
}

func NewTrackRouteHandler(svc PositionTrackingService) *TrackRouteHandler {
	return &TrackRouteHandler{svc: svc}
}

func (h *TrackRouteHandler) Handle(c *gin.Context) {
	routeID := c.Param("routeId")
	conn, err := upgrader.Upgrade(c.Writer, c.Request, nil)
	if err != nil {
		return
	}
	defer conn.Close()

	ctx, cancel := context.WithCancel(c.Request.Context())
	go readPump(cancel, conn)
	h.streamPosition(ctx, conn, routeID)
}

func (h *TrackRouteHandler) streamPosition(ctx context.Context, conn *websocket.Conn, routeID string) {
	// TODO
}

func readPump(cancel context.CancelFunc, conn *websocket.Conn) {
	defer cancel()
	for {
		if _, _, err := conn.ReadMessage(); err != nil {
			return
		}
	}
}
