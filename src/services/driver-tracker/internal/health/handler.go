package health

import (
	"net/http"

	redisclient "driver-tracker/internal/redis"

	"github.com/gin-gonic/gin"
)

type Handler struct {
	redis *redisclient.Client
}

func NewHandler(redis *redisclient.Client) *Handler {
	return &Handler{redis: redis}
}

func (h *Handler) Handle(c *gin.Context) {
	if err := h.redis.Start(c.Request.Context()); err != nil {
		c.Status(http.StatusServiceUnavailable)
		return
	}
	c.Status(http.StatusOK)
}