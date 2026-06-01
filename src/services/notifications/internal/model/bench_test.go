package model

import (
	"fmt"
	"testing"
)

// BenchmarkSendConnected measures Send to a user with a draining connection.
func BenchmarkSendConnected(b *testing.B) {
	m := NewUsersMapper()
	ch := m.Register("u1")
	defer m.Unregister("u1", ch)

	// drain so the buffer never fills
	go func() {
		for range ch {
		}
	}()

	n := Notification{Type: "ride_started"}
	for b.Loop() {
		m.Send("u1", n)
	}
}

// BenchmarkSendDisconnected measures the no-op path (user not connected).
func BenchmarkSendDisconnected(b *testing.B) {
	m := NewUsersMapper()
	n := Notification{Type: "ride_started"}
	for b.Loop() {
		m.Send("ghost", n)
	}
}

// BenchmarkSendParallel measures contended Send across many users.
func BenchmarkSendParallel(b *testing.B) {
	m := NewUsersMapper()
	const users = 256
	for u := range users {
		id := fmt.Sprintf("u%d", u)
		ch := m.Register(id)
		go func() {
			for range ch {
			}
		}()
	}

	b.ResetTimer()
	b.RunParallel(func(pb *testing.PB) {
		i := 0
		n := Notification{Type: "x"}
		for pb.Next() {
			m.Send(fmt.Sprintf("u%d", i%users), n)
			i++
		}
	})
}