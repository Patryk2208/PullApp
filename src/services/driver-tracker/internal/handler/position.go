package handler

import (
	"context"
	"net/http"

	"driver-tracker/internal/model"

	"github.com/gin-gonic/gin"
)

type PositionUpdateService interface {
	UpdateDriverPosition(ctx context.Context, req model.PositionRequest) error
}

type PositionUpdateHandler struct {
	svc PositionUpdateService
}

func NewPostPositionHandler(svc PositionUpdateService) *PositionUpdateHandler {
	return &PositionUpdateHandler{svc: svc}
}

func (h *PositionUpdateHandler) Handle(c *gin.Context) {
	var req model.PositionRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.Status(http.StatusBadRequest)
		return
	}

	if err := h.svc.UpdateDriverPosition(c.Request.Context(), req); err != nil {
		c.Status(http.StatusInternalServerError)
		return
	}

	c.Status(http.StatusNoContent)
}
