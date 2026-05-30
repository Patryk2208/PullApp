package utils

import (
	"testing"
	"time"
)

func TestWriteWithBackoff_SucceedsImmediately(t *testing.T) {
	ch := make(chan int, 1)
	ok := WriteWithBackoff(ch, 42, time.Millisecond, time.Second)
	if !ok {
		t.Fatal("expected true for buffered channel with room")
	}
	if got := <-ch; got != 42 {
		t.Errorf("got %d, want 42", got)
	}
}

func TestWriteWithBackoff_ReturnsFalseWhenChannelStaysFull(t *testing.T) {
	ch := make(chan int, 1)
	ch <- 0 // fill the only slot

	start := time.Now()
	ok := WriteWithBackoff(ch, 1, time.Millisecond, 2*time.Millisecond)
	elapsed := time.Since(start)

	if ok {
		t.Error("expected false when channel stays full")
	}
	// should return in a few ms, not seconds
	if elapsed > 200*time.Millisecond {
		t.Errorf("took too long: %v (expected backoff to expire quickly)", elapsed)
	}
	// original value must still be there, not overwritten
	if got := <-ch; got != 0 {
		t.Errorf("channel value corrupted: got %d", got)
	}
}

func TestWriteWithBackoff_SucceedsAfterReceiverDrains(t *testing.T) {
	ch := make(chan int, 1)
	ch <- 0 // fill it

	// drain after a short delay
	go func() {
		time.Sleep(20 * time.Millisecond)
		<-ch
	}()

	ok := WriteWithBackoff(ch, 99, time.Millisecond, time.Second)
	if !ok {
		t.Fatal("expected true once channel was drained")
	}
	if got := <-ch; got != 99 {
		t.Errorf("got %d, want 99", got)
	}
}

func TestWriteWithBackoff_ZeroCapacityChannel(t *testing.T) {
	ch := make(chan int) // unbuffered, no receiver

	ok := WriteWithBackoff(ch, 1, time.Millisecond, 2*time.Millisecond)
	if ok {
		t.Error("expected false for unbuffered channel with no receiver")
	}
}

func TestWriteWithBackoff_DeliversToDifferentTypes(t *testing.T) {
	type point struct{ x, y float64 }
	ch := make(chan point, 1)

	ok := WriteWithBackoff(ch, point{1.0, 2.0}, time.Millisecond, time.Second)
	if !ok {
		t.Fatal("expected true")
	}
	if got := <-ch; got.x != 1.0 || got.y != 2.0 {
		t.Errorf("got %+v", got)
	}
}
