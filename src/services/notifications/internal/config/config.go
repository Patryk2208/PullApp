package config

import "os"

type Config struct {
	PostgresURL                    string
	RedisAddr                      string
	RedisPassword                  string
	KafkaBrokers                   string
	KafkaGroupID                   string
	KafkaTopicNotificationTriggers string
	KafkaTopicRideCompletions      string
	KafkaTopicUserActions          string
	HTTPAddr                       string
	FirebaseCredentialsFile        string
	FirebaseProjectID              string
}

func Load() Config {
	return Config{
		PostgresURL:                    getEnv("POSTGRES_URL", "postgres://notifications:notifications@localhost:5432/notifications"),
		RedisAddr:                      getEnv("REDIS_ADDR", "localhost:6381"),
		RedisPassword:                  getEnv("REDIS_PASSWORD", ""),
		KafkaBrokers:                   getEnv("KAFKA_BROKERS", "localhost:9092"),
		KafkaGroupID:                   getEnv("KAFKA_GROUP_ID", "notifications"),
		KafkaTopicNotificationTriggers: getEnv("KAFKA_TOPIC_NOTIFICATION_TRIGGERS", "notification-triggers"),
		KafkaTopicRideCompletions:      getEnv("KAFKA_TOPIC_RIDE_COMPLETIONS", "ride-completions"),
		KafkaTopicUserActions:          getEnv("KAFKA_TOPIC_USER_ACTIONS", "user-actions"),
		HTTPAddr:                       getEnv("HTTP_ADDR", ":8080"),
		FirebaseCredentialsFile:        getEnv("FIREBASE_CREDENTIALS_FILE", "/secrets/firebase.json"),
		FirebaseProjectID:              getEnv("FIREBASE_PROJECT_ID", ""),
	}
}

func getEnv(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}
