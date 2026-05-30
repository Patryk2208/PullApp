package service

import "notifications/internal/model"

type Streamer struct {
	mapper *model.UsersMapper
}

func NewStreamer(mapper *model.UsersMapper) *Streamer {
	return &Streamer{mapper: mapper}
}

func (s *Streamer) Send(userID string, env model.Envelope) {
	s.mapper.Send(userID, model.Notification{
		Type:    env.EventType,
		Payload: env.Payload,
	})
}
