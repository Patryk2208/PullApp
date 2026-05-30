package utils

import (
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/gin-gonic/gin"
)

func init() {
	gin.SetMode(gin.TestMode)
}

type testPayload struct {
	Name  string  `json:"name"  binding:"required"`
	Value float64 `json:"value" binding:"required"`
}

func bindTestEngine() *gin.Engine {
	r := gin.New()
	r.POST("/bind", func(c *gin.Context) {
		var p testPayload
		if err := BindStrictJSON(c, &p); err != nil {
			c.Status(http.StatusBadRequest)
			return
		}
		c.Status(http.StatusOK)
	})
	return r
}

func bindRequest(r *gin.Engine, body string) int {
	w := httptest.NewRecorder()
	req := httptest.NewRequest(http.MethodPost, "/bind", strings.NewReader(body))
	req.Header.Set("Content-Type", "application/json")
	r.ServeHTTP(w, req)
	return w.Code
}

func TestBindStrictJSON_ValidBody(t *testing.T) {
	r := bindTestEngine()
	if code := bindRequest(r, `{"name":"alice","value":3.14}`); code != http.StatusOK {
		t.Errorf("status = %d, want 200", code)
	}
}

func TestBindStrictJSON_UnknownFieldRejected(t *testing.T) {
	r := bindTestEngine()
	body := `{"name":"alice","value":3.14,"unexpected":true}`
	if code := bindRequest(r, body); code != http.StatusBadRequest {
		t.Errorf("status = %d, want 400 for unknown field", code)
	}
}

func TestBindStrictJSON_MissingRequiredField(t *testing.T) {
	r := bindTestEngine()
	// 'value' missing → zero float64 → binding:required fails
	if code := bindRequest(r, `{"name":"alice"}`); code != http.StatusBadRequest {
		t.Errorf("status = %d, want 400 for missing required field", code)
	}
}

func TestBindStrictJSON_EmptyBodyRejected(t *testing.T) {
	r := bindTestEngine()
	if code := bindRequest(r, ``); code != http.StatusBadRequest {
		t.Errorf("status = %d, want 400 for empty body", code)
	}
}

func TestBindStrictJSON_MalformedJSON(t *testing.T) {
	r := bindTestEngine()
	if code := bindRequest(r, `{not valid json`); code != http.StatusBadRequest {
		t.Errorf("status = %d, want 400 for malformed JSON", code)
	}
}

func TestBindStrictJSON_AllFieldsMissing(t *testing.T) {
	r := bindTestEngine()
	if code := bindRequest(r, `{}`); code != http.StatusBadRequest {
		t.Errorf("status = %d, want 400 when all required fields are missing", code)
	}
}
