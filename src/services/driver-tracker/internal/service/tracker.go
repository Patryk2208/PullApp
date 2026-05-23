package service

import (
	"context"
	"driver-tracker/internal/model"
)

type TrackingRepository interface {
}

type TrackerService struct {
	repo TrackingRepository
}

func NewTrackerService(repo TrackingRepository) *TrackerService {
	return &TrackerService{repo: repo}
}

func (s *TrackerService) UpdateDriverPosition(ctx context.Context, req model.PositionRequest) error {
	// todo
}

func (s *TrackerService) GetAllDriversNearby(ctx context.Context) error {
	// todo
}
