package handler

import (
	"fmt"
	"log"
	"net/http"
	"time"

	"notifications/internal/model"
)

type StreamerHandler struct {
	mapper *model.UsersMapper
}

func NewStreamerHandler(mapper *model.UsersMapper) *StreamerHandler {
	return &StreamerHandler{mapper: mapper}
}

func (h *StreamerHandler) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	// auth is handled upstream by the gateway, which sets the user id header.
	userID := r.Header.Get("X-User-Id")
	if userID == "" {
		w.WriteHeader(http.StatusUnauthorized)
		return
	}

	flusher, ok := w.(http.Flusher)
	if !ok {
		http.Error(w, "streaming unsupported", http.StatusInternalServerError)
		return
	}

	w.Header().Set("Content-Type", "text/event-stream")
	w.Header().Set("Cache-Control", "no-cache")
	w.Header().Set("Connection", "keep-alive")
	w.Header().Set("X-Accel-Buffering", "no")

	ch := h.mapper.Register(userID)
	log.Printf("sse: user=%s connected ip=%s", userID, r.RemoteAddr)
	defer func() {
		h.mapper.Unregister(userID, ch)
		log.Printf("sse: user=%s disconnected", userID)
	}()

	// tell the client to reconnect 3s after a drop
	fmt.Fprint(w, "retry: 3000\n\n")
	flusher.Flush()

	keepalive := time.NewTicker(15 * time.Second)
	defer keepalive.Stop()

	for {
		select {
		case <-r.Context().Done():
			return
		case <-keepalive.C:
			// comment line keeps proxies from closing an idle connection
			fmt.Fprint(w, ": keepalive\n\n")
			flusher.Flush()
		case n, ok := <-ch:
			if !ok {
				return
			}
			log.Printf("sse: user=%s sending type=%s", userID, n.Type)
			fmt.Fprintf(w, "event: %s\ndata: %s\n\n", n.Type, n.Payload)
			flusher.Flush()
		}
	}
}
