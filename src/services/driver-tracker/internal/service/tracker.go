package service

import (
	"context"
	"time"

	"github.com/bytedance/gopkg/util/logger"
	"github.com/google/uuid"

	"driver-tracker/internal/model"
	"driver-tracker/internal/utils"
)

type TrackingRepository interface {
	GetRouteDriverPosition(ctx context.Context, routeId uuid.UUID) (model.DriverPosition, error)
	SetRouteDriverPosition(ctx context.Context, routeId uuid.UUID, point model.GeoPoint) error
}

type TrackerService struct {
	inputChannel  chan model.RoutePositionRequest
	outputChannel chan model.DriverPosition
	repo          TrackingRepository
}

func NewTrackerService(repo TrackingRepository) *TrackerService {
	inCh := make(chan model.RoutePositionRequest, 128)
	outCh := make(chan model.DriverPosition, 128)
	return &TrackerService{
		repo:          repo,
		inputChannel:  inCh,
		outputChannel: outCh,
	}
}

func (ts *TrackerService) GetInputChannel() chan model.RoutePositionRequest {
	return ts.inputChannel
}

func (ts *TrackerService) GetOutputChannel() chan model.DriverPosition {
	return ts.outputChannel
}

func (ts *TrackerService) UpdateDriverPosition(ctx context.Context, routeId uuid.UUID, point model.GeoPoint) error {
	return ts.repo.SetRouteDriverPosition(ctx, routeId, point)
}

func (ts *TrackerService) Run(ctx context.Context) {
	for {
		select {
		case req := <-ts.inputChannel:
			pos, err := ts.repo.GetRouteDriverPosition(ctx, req.RouteId)
			if err != nil {
				logger.Errorf("Failed to get route driver position: %v", err)
				continue
			}
			utils.WriteWithBackoff(ts.outputChannel, pos, time.Millisecond*50, time.Second)
		case <-ctx.Done():
			return
		}
	}
}