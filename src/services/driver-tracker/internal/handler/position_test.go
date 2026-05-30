package handler

import (
	"context"
	"errors"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"

	"driver-tracker/internal/model"
)

func init() {
	gin.SetMode(gin.TestMode)
}

type mockPositionSvc struct {
	err error
}

func (m *mockPositionSvc) UpdateDriverPosition(_ context.Context, _ uuid.UUID, _ model.GeoPoint) error {
	return m.err
}

func setupPositionRouter(svc PositionUpdateService) *gin.Engine {
	r := gin.New()
	r.POST("/position/:routeId", NewPostPositionHandler(svc).Handle)
	return r
}

func doRequest(r *gin.Engine, method, path, body string) *httptest.ResponseRecorder {
	w := httptest.NewRecorder()
	req := httptest.NewRequest(method, path, strings.NewReader(body))
	req.Header.Set("Content-Type", "application/json")
	r.ServeHTTP(w, req)
	return w
}

func TestPositionHandler_ValidRequest(t *testing.T) {
	r := setupPositionRouter(&mockPositionSvc{})
	id := uuid.New()
	body := `{"lat":52.2297,"lng":21.0122,"timestamp":1000}`

	w := doRequest(r, http.MethodPost, "/position/"+id.String(), body)
	if w.Code != http.StatusNoContent {
		t.Errorf("status = %d, want 204", w.Code)
	}
}

func TestPositionHandler_InvalidRouteId(t *testing.T) {
	r := setupPositionRouter(&mockPositionSvc{})
	body := `{"lat":52.2297,"lng":21.0122,"timestamp":1000}`

	w := doRequest(r, http.MethodPost, "/position/not-a-uuid", body)
	if w.Code != http.StatusBadRequest {
		t.Errorf("status = %d, want 400", w.Code)
	}
}

func TestPositionHandler_MissingRequiredFields(t *testing.T) {
	r := setupPositionRouter(&mockPositionSvc{})
	id := uuid.New()

	// all required fields missing — validator must reject zero values
	w := doRequest(r, http.MethodPost, "/position/"+id.String(), `{}`)
	if w.Code != http.StatusBadRequest {
		t.Errorf("status = %d, want 400", w.Code)
	}
}

func TestPositionHandler_UnknownFieldsRejected(t *testing.T) {
	r := setupPositionRouter(&mockPositionSvc{})
	id := uuid.New()
	body := `{"lat":52.2,"lng":21.0,"timestamp":1000,"extra":"x"}`

	w := doRequest(r, http.MethodPost, "/position/"+id.String(), body)
	if w.Code != http.StatusBadRequest {
		t.Errorf("status = %d, want 400 for unknown fields", w.Code)
	}
}

func TestPositionHandler_MalformedJSON(t *testing.T) {
	r := setupPositionRouter(&mockPositionSvc{})
	id := uuid.New()

	w := doRequest(r, http.MethodPost, "/position/"+id.String(), `{not json`)
	if w.Code != http.StatusBadRequest {
		t.Errorf("status = %d, want 400", w.Code)
	}
}

func TestPositionHandler_ServiceError(t *testing.T) {
	r := setupPositionRouter(&mockPositionSvc{err: errors.New("redis down")})
	id := uuid.New()
	body := `{"lat":52.2297,"lng":21.0122,"timestamp":1000}`

	w := doRequest(r, http.MethodPost, "/position/"+id.String(), body)
	if w.Code != http.StatusInternalServerError {
		t.Errorf("status = %d, want 500", w.Code)
	}
}

func TestPositionHandler_CoordinatesStoredCorrectly(t *testing.T) {
	var capturedPoint model.GeoPoint
	var capturedRoute uuid.UUID

	svc := &capturingSvc{onUpdate: func(id uuid.UUID, p model.GeoPoint) {
		capturedRoute = id
		capturedPoint = p
	}}
	r := gin.New()
	r.POST("/position/:routeId", NewPostPositionHandler(svc).Handle)

	id := uuid.New()
	body := `{"lat":52.2297,"lng":21.0122,"timestamp":1000}`
	w := doRequest(r, http.MethodPost, "/position/"+id.String(), body)
	if w.Code != http.StatusNoContent {
		t.Fatalf("status = %d", w.Code)
	}
	if capturedRoute != id {
		t.Errorf("routeId = %v, want %v", capturedRoute, id)
	}
	if capturedPoint.Lat != 52.2297 || capturedPoint.Lng != 21.0122 {
		t.Errorf("point = %+v", capturedPoint)
	}
}

type capturingSvc struct {
	onUpdate func(uuid.UUID, model.GeoPoint)
}

func (c *capturingSvc) UpdateDriverPosition(_ context.Context, id uuid.UUID, p model.GeoPoint) error {
	c.onUpdate(id, p)
	return nil
}
