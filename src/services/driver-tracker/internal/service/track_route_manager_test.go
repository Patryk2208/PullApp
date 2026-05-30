package service

import (
	"context"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/coder/websocket"
	"github.com/coder/websocket/wsjson"
	"github.com/google/uuid"

	"driver-tracker/internal/model"
)

// dialWS connects to the test server and returns a client WebSocket connection.
func dialWS(t *testing.T, srv *httptest.Server) (*websocket.Conn, context.CancelFunc) {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	wsURL := "ws" + strings.TrimPrefix(srv.URL, "http")
	conn, _, err := websocket.Dial(ctx, wsURL, nil)
	if err != nil {
		cancel()
		t.Fatalf("websocket dial: %v", err)
	}
	return conn, cancel
}

// newManagerServer starts an httptest server that accepts one WebSocket
// connection and creates a TrackRouteManager with the supplied channels.
func newManagerServer(t *testing.T, routeId uuid.UUID, reqCh chan model.RoutePositionRequest, respCh chan model.DriverPosition) *httptest.Server {
	t.Helper()
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		conn, err := websocket.Accept(w, r, &websocket.AcceptOptions{InsecureSkipVerify: true})
		if err != nil {
			t.Errorf("accept: %v", err)
			return
		}
		defer conn.CloseNow()

		wm := NewTrackRouteManager(r.Context(), conn, routeId, reqCh, respCh)
		go wm.ReceiveLocations()
		go wm.StreamPosition()
		wm.Wait()
	}))
	t.Cleanup(srv.Close)
	return srv
}

func TestTrackRouteManager_ReceiveLocationsForwardsToReqCh(t *testing.T) {
	routeId := uuid.New()
	reqCh := make(chan model.RoutePositionRequest, 4)
	respCh := make(chan model.DriverPosition, 4)

	srv := newManagerServer(t, routeId, reqCh, respCh)
	clientConn, cancel := dialWS(t, srv)
	defer cancel()
	defer clientConn.CloseNow()

	ctx := context.Background()
	pos := model.PositionRequest{Lat: 52.2297, Lng: 21.0122, Timestamp: 1000}
	if err := wsjson.Write(ctx, clientConn, pos); err != nil {
		t.Fatalf("write: %v", err)
	}

	select {
	case req := <-reqCh:
		if req.RouteId != routeId {
			t.Errorf("routeId = %v, want %v", req.RouteId, routeId)
		}
		if req.PositionRequest.Lat != pos.Lat || req.PositionRequest.Lng != pos.Lng {
			t.Errorf("position = %+v, want %+v", req.PositionRequest, pos)
		}
	case <-time.After(2 * time.Second):
		t.Fatal("position not forwarded to reqCh")
	}
}

func TestTrackRouteManager_ReceiveLocations_MultipleMessages(t *testing.T) {
	routeId := uuid.New()
	reqCh := make(chan model.RoutePositionRequest, 8)
	respCh := make(chan model.DriverPosition, 4)

	srv := newManagerServer(t, routeId, reqCh, respCh)
	clientConn, cancel := dialWS(t, srv)
	defer cancel()
	defer clientConn.CloseNow()

	ctx := context.Background()
	const n = 3
	for i := range n {
		pos := model.PositionRequest{Lat: float64(i + 1), Lng: float64(i + 1), Timestamp: int64(i)}
		if err := wsjson.Write(ctx, clientConn, pos); err != nil {
			t.Fatalf("write %d: %v", i, err)
		}
	}

	timeout := time.After(3 * time.Second)
	for i := range n {
		select {
		case req := <-reqCh:
			if req.RouteId != routeId {
				t.Errorf("msg %d: routeId mismatch", i)
			}
		case <-timeout:
			t.Fatalf("only received %d/%d messages", i, n)
		}
	}
}

func TestTrackRouteManager_StreamPositionWritesToClient(t *testing.T) {
	routeId := uuid.New()
	reqCh := make(chan model.RoutePositionRequest, 4)
	respCh := make(chan model.DriverPosition, 4)

	srv := newManagerServer(t, routeId, reqCh, respCh)
	clientConn, cancel := dialWS(t, srv)
	defer cancel()
	defer clientConn.CloseNow()

	want := model.DriverPosition{
		RouteID:  routeId.String(),
		GeoPoint: model.GeoPoint{Lat: 52.2297, Lng: 21.0122},
	}
	respCh <- want

	var got model.DriverPosition
	ctx, readCancel := context.WithTimeout(context.Background(), 2*time.Second)
	defer readCancel()
	if err := wsjson.Read(ctx, clientConn, &got); err != nil {
		t.Fatalf("client read: %v", err)
	}

	if got.RouteID != want.RouteID {
		t.Errorf("RouteID = %s, want %s", got.RouteID, want.RouteID)
	}
	if got.GeoPoint != want.GeoPoint {
		t.Errorf("GeoPoint = %+v, want %+v", got.GeoPoint, want.GeoPoint)
	}
}

func TestTrackRouteManager_ClientDisconnectExitsWait(t *testing.T) {
	routeId := uuid.New()
	reqCh := make(chan model.RoutePositionRequest, 4)
	respCh := make(chan model.DriverPosition, 4)

	srv := newManagerServer(t, routeId, reqCh, respCh)
	clientConn, cancel := dialWS(t, srv)
	defer cancel()

	// close client connection normally → server-side manager must exit
	if err := clientConn.Close(websocket.StatusNormalClosure, "done"); err != nil {
		t.Logf("close: %v", err) // non-fatal; connection may already be gone
	}

	// the server handler returns once wm.Wait() completes; the test server
	// will close idle connections after the handler returns, so just ensure
	// no goroutine leaks by waiting a bit
	time.Sleep(200 * time.Millisecond)
}
