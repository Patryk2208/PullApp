package utils

import (
	"time"

	"github.com/bytedance/gopkg/util/logger"
)

func WriteWithBackoff[T any](channel chan T, payload T, backoff time.Duration, maxBackoff time.Duration) bool {
	for {
		select {
		case channel <- payload:
			return true
		default:
			logger.Info("ws read request channel full")
			time.Sleep(backoff)
			backoff *= 2
			if backoff > maxBackoff {
				return false
			}
		}
	}
}
