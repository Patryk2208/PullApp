package handler

import (
	"context"
	"encoding/json"
	"net/http"
)

type deviceRepository interface {
	UpsertDeviceToken(ctx context.Context, userID, token, platform string) error
}

type RegisterHandler struct {
	repo deviceRepository
}

func NewRegisterHandler(repo deviceRepository) *RegisterHandler {
	return &RegisterHandler{repo: repo}
}

func (h *RegisterHandler) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodPost {
		w.WriteHeader(http.StatusMethodNotAllowed)
		return
	}

	var body struct {
		UserID   string `json:"userId"`
		Token    string `json:"token"`
		Platform string `json:"platform"`
	}
	if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
		w.WriteHeader(http.StatusBadRequest)
		return
	}
	if body.UserID == "" || body.Token == "" || body.Platform == "" {
		w.WriteHeader(http.StatusBadRequest)
		return
	}

	if err := h.repo.UpsertDeviceToken(r.Context(), body.UserID, body.Token, body.Platform); err != nil {
		w.WriteHeader(http.StatusInternalServerError)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}