package handler

import (
	"driver-tracker/internal/model"
	"net/http"

	"github.com/coder/websocket"
	"github.com/gin-gonic/gin"
	"github.com/google/uuid"

	"driver-tracker/internal/service"
)

type RouteDriverTracker interface {
	GetInputChannel() chan model.RoutePositionRequest
	GetOutputChannel() chan model.DriverPosition
}

type TrackRouteHandler struct {
	service RouteDriverTracker
}

func NewTrackRouteHandler(service RouteDriverTracker) *TrackRouteHandler {
	return &TrackRouteHandler{
		service: service,
	}
}

func (h *TrackRouteHandler) Handle(c *gin.Context) {
	idStr := c.Param("routeId")
	id, err := uuid.Parse(idStr)
	if err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": err.Error()})
		return
	}

	conn, err := websocket.Accept(c.Writer, c.Request, &websocket.AcceptOptions{
		InsecureSkipVerify: true,
	})
	if err != nil {
		return
	}
	defer conn.CloseNow()

	wm := service.NewTrackRouteManager(c.Request.Context(), conn, id, h.service.GetInputChannel(), h.service.GetOutputChannel())

	go wm.ReceiveLocations()
	go wm.StreamPosition()

	wm.Wait()
}
