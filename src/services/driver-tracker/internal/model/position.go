package model

import "github.com/google/uuid"

// PositionRequest is the body of POST /position.
type PositionRequest struct {
	Lat       float64 `json:"lat"        binding:"required"`
	Lng       float64 `json:"lng"        binding:"required"`
	Timestamp int64   `json:"timestamp"  binding:"required"`
}

type RoutePositionRequest struct {
	RouteId         uuid.UUID
	PositionRequest PositionRequest
}

// GeoPoint is a WGS-84 coordinate pair compatible with PostGIS geometry(Point,4326).
// Longitude comes first to match the PostGIS / GeoJSON axis order.
type GeoPoint struct {
	Lng float64 `json:"lng"`
	Lat float64 `json:"lat"`
}

// DriverPosition pairs a driver's routeId with their current location.
type DriverPosition struct {
	RouteID  string   `json:"routeId"`
	GeoPoint GeoPoint `json:"geoPoint"`
}

// NearbyDriversResponse is sent to the passenger on each WebSocket tick.
type NearbyDriversResponse struct {
	Drivers []DriverPosition `json:"drivers"`
}
