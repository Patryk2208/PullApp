package config

import "os"

type Config struct {
	PostgresURL  string
	KafkaBrokers string
	KafkaTopic   string
	KafkaGroupID string
	HTTPAddr     string
}

func Load() Config {
	return Config{
		PostgresURL:  getEnv("POSTGRES_URL", "postgres://notifications:notifications@localhost:5432/notifications"),
		KafkaBrokers: getEnv("KAFKA_BROKERS", "localhost:9092"),
		KafkaTopic:   getEnv("KAFKA_TOPIC", "notifications"),
		KafkaGroupID: getEnv("KAFKA_GROUP_ID", "notifications"),
		HTTPAddr:     getEnv("HTTP_ADDR", ":8080"),
	}
}

func getEnv(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}