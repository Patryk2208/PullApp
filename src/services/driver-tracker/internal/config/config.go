package config

import "os"

type Config struct {
	RedisAddr     string
	RedisPassword string
	HTTPAddr      string
}

func Load() Config {
	return Config{
		RedisAddr:     getEnv("REDIS_ADDR", "localhost:6379"),
		RedisPassword: getEnv("REDIS_PASSWORD", ""),
		HTTPAddr:      getEnv("HTTP_ADDR", ":8080"),
	}
}

func getEnv(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}
