package model

import (
	"log"
	"sync"
)

// UsersMapper holds one buffered channel per connected user. The SSE handler
// registers a channel on connect; the streamer pushes DTOs into it.
type UsersMapper struct {
	UsersMap map[string]chan Notification
	Mutex    sync.RWMutex
}

func NewUsersMapper() *UsersMapper {
	return &UsersMapper{UsersMap: make(map[string]chan Notification)}
}

// Register opens a buffered channel for userID and returns it. If the user
// already has a connection (e.g. a second tab), the old channel is closed so
// its handler exits. The caller must Unregister the returned channel when the
// HTTP connection ends.
func (m *UsersMapper) Register(userID string) <-chan Notification {
	ch := make(chan Notification, 32)

	m.Mutex.Lock()
	if old, ok := m.UsersMap[userID]; ok {
		close(old)
	}
	m.UsersMap[userID] = ch
	m.Mutex.Unlock()

	return ch
}

// Unregister removes and closes userID's channel when the SSE connection ends.
// Only tears down if ch still owns the slot — a newer connection may have
// replaced it, in which case Register already closed ch.
func (m *UsersMapper) Unregister(userID string, ch <-chan Notification) {
	m.Mutex.Lock()
	defer m.Mutex.Unlock()

	if cur, ok := m.UsersMap[userID]; ok && cur == ch {
		delete(m.UsersMap, userID)
		close(cur)
	}
}

// Send does a non-blocking delivery to userID's channel. No-op if the user is
// not connected; drops the message if the channel is full (client reconnects
// and re-fetches state). The send happens under the read lock so it cannot race
// with a channel being closed in Register/Unregister.
func (m *UsersMapper) Send(userID string, n Notification) {
	m.Mutex.RLock()
	defer m.Mutex.RUnlock()

	ch, ok := m.UsersMap[userID]
	if !ok {
		log.Printf("User %s not found", userID)
		return
	}
	log.Printf("Sending notification to %s", userID)
	select {
	case ch <- n:
	default:
	}
}
