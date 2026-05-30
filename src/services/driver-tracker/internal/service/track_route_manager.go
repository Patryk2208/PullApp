package service

import (
	"context"
	"driver-tracker/internal/utils"
	"errors"
	"sync"
	"time"

	"github.com/bytedance/gopkg/util/logger"
	"github.com/coder/websocket"
	"github.com/coder/websocket/wsjson"
	"github.com/google/uuid"

	"driver-tracker/internal/model"
)

type TrackRouteManager struct {
	ctx     context.Context
	cancel  context.CancelFunc
	conn    *websocket.Conn
	wg      *sync.WaitGroup
	routeId uuid.UUID
	reqCh   chan model.RoutePositionRequest
	respCh  chan model.DriverPosition
}

func NewTrackRouteManager(
	ctx context.Context,
	conn *websocket.Conn,
	uuid uuid.UUID,
	reqCh chan model.RoutePositionRequest,
	respCh chan model.DriverPosition,
) *TrackRouteManager {
	ctx, cancel := context.WithCancel(ctx)
	wg := new(sync.WaitGroup)
	wg.Add(2) // reader and writer

	return &TrackRouteManager{
		ctx:     ctx,
		cancel:  cancel,
		conn:    conn,
		wg:      wg,
		routeId: uuid,
		reqCh:   reqCh,
		respCh:  respCh,
	}
}

func (wm *TrackRouteManager) Wait() {
	wm.wg.Wait()
}

func (wm *TrackRouteManager) ReceiveLocations() {
	defer wm.wg.Done()
	defer wm.cancel()

	for {
		var pos model.PositionRequest
		if err := wsjson.Read(wm.ctx, wm.conn, &pos); err != nil {
			if errors.Is(err, context.Canceled) || errors.Is(err, context.DeadlineExceeded) {
				logger.Info("Context cancelled for ws read")
				return
			}
			switch websocket.CloseStatus(err) {
			case websocket.StatusNormalClosure, websocket.StatusGoingAway:
				logger.Info("ws closed normally")
			default:
				logger.Errorf("ws read error: %v", err)
			}
			return
		}

		req := model.RoutePositionRequest{
			RouteId:         wm.routeId,
			PositionRequest: pos,
		}

		utils.WriteWithBackoff(wm.reqCh, req, time.Millisecond*50, time.Second)
	}
}

func (wm *TrackRouteManager) StreamPosition() {
	defer wm.wg.Done()

	for {
		select {
		case resp := <-wm.respCh:
			err := wsjson.Write(wm.ctx, wm.conn, resp)
			if err != nil {
				if errors.Is(err, context.Canceled) || errors.Is(err, context.DeadlineExceeded) {
					logger.Info("Context cancelled for ws write")
					return
				}
				logger.Errorf("ws write error: %v", err)
				return
			}
		case <-wm.ctx.Done():
			return
		}
	}
}
