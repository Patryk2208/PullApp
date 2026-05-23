package handler

import (
	"context"
	"net/http"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"

	"driver-tracker/internal/model"
	"driver-tracker/internal/utils"
)

type PositionUpdateService interface {
	UpdateDriverPosition(ctx context.Context, routeId uuid.UUID, point model.GeoPoint) error
}

type PositionUpdateHandler struct {
	svc PositionUpdateService
}

func NewPostPositionHandler(svc PositionUpdateService) *PositionUpdateHandler {
	return &PositionUpdateHandler{svc: svc}
}

func (h *PositionUpdateHandler) Handle(c *gin.Context) {
	routeId, err := uuid.Parse(c.Param("routeId"))
	if err != nil {
		c.Status(http.StatusBadRequest)
		return
	}

	var req model.PositionRequest
	if err := utils.BindStrictJSON(c, &req); err != nil {
		c.Status(http.StatusBadRequest)
		return
	}

	point := model.GeoPoint{Lat: req.Lat, Lng: req.Lng}
	if err := h.svc.UpdateDriverPosition(c.Request.Context(), routeId, point); err != nil {
		c.Status(http.StatusInternalServerError)
		return
	}

	c.Status(http.StatusNoContent)
}