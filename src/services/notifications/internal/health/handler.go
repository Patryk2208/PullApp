package health

import (
	"net/http"

	"github.com/jackc/pgx/v5/pgxpool"
	"github.com/segmentio/kafka-go"
)

func NewHandler(db *pgxpool.Pool, kafkaBroker string) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if err := db.Ping(r.Context()); err != nil {
			w.WriteHeader(http.StatusServiceUnavailable)
			return
		}
		conn, err := kafka.DialContext(r.Context(), "tcp", kafkaBroker)
		if err != nil {
			w.WriteHeader(http.StatusServiceUnavailable)
			return
		}
		_ = conn.Close()
		w.WriteHeader(http.StatusOK)
	})
}