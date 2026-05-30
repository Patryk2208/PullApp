package config

import "os"

type Config struct {
	PostgresURL             string
	KafkaBrokers            string
	KafkaGroupID            string
	HTTPAddr                string
	FirebaseCredentialsFile string
	FirebaseProjectID       string
}

func Load() Config {
	return Config{
		PostgresURL:             getEnv("POSTGRES_URL", "postgres://notifications:notifications@localhost:5432/notifications"),
		KafkaBrokers:            getEnv("KAFKA_BROKERS", "localhost:9092"),
		KafkaGroupID:            getEnv("KAFKA_GROUP_ID", "notifications"),
		HTTPAddr:                getEnv("HTTP_ADDR", ":8080"),
		FirebaseCredentialsFile: getEnv("FIREBASE_CREDENTIALS_FILE", "/secrets/firebase.json"),
		FirebaseProjectID:       getEnv("FIREBASE_PROJECT_ID", ""),
	}
}

func getEnv(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}