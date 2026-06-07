import { useState, useEffect } from 'react';
import { RideMatchingQuery, TripMatch } from '@pullapp/domain';
import { useAuthStore } from '../auth/authStore'; 

export const useSearchTrips = () => {
    const [isLoading, setIsLoading] = useState(false);
    const [jobId, setJobId] = useState<string | null>(null);
    const [matches, setMatches] = useState<TripMatch[]>([]);
    const [error, setError] = useState<string | null>(null);

    const token = useAuthStore(state => state.token);

    const searchTrips = async (query: RideMatchingQuery) => {
        setIsLoading(true);
        setError(null);
        setMatches([]);
        setJobId(null);

        try {
            const payload = {
                Start: { Latitude: query.start.lat, Longitude: query.start.lng },
                End: { Latitude: query.end.lat, Longitude: query.end.lng },
                DepartureDate: new Date(query.departureDate).getTime(),
                SeatsNeeded: query.seatsNeeded,
                MaxDetourKm: query.maxDetourKm,
                TimeWindowMinutes: query.timeWindowMinutes
            };

            console.log("Token wysyłany:", token);
            console.log("Auth header:", token ? `Bearer ${token}` : "BRAK");
            console.log("Payload wysyłany:", JSON.stringify(payload));
            const response = await fetch('/api/route/passenger/routes/search', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...(token ? { 'Authorization': `Bearer ${token}` } : {})
                },
                body: JSON.stringify(payload)
            });

            if (!response.ok) {
                throw new Error(`API odrzuciło żądanie: ${response.status}`);
            }

            const data = await response.json();
            console.log("Otrzymano odpowiedź z Trip-Plannera:", data);

            if (data.jobId) {
                setJobId(data.jobId);
            }
        } catch (err: any) {
            console.error("Błąd POST:", err);
            setError(err.message);
            setIsLoading(false);
        }
    };

    // Efekt nasłuchujący SSE z serwisu Notifications
    useEffect(() => {
        if (!jobId || !token) return;

        console.log(`Otwieram strumień SSE dla zadania: ${jobId}`);

        const controller = new AbortController();

        const connectSSE = async () => {
            try {
                const response = await fetch('/sse/notifications', {
                    headers: {
                        'Authorization': `Bearer ${token}`,
                        'Accept': 'text/event-stream',
                    },
                    signal: controller.signal,
                });

                if (!response.ok || !response.body) {
                    console.error("Błąd SSE:", response.status);
                    setIsLoading(false);
                    return;
                }

                const reader = response.body.getReader();
                const decoder = new TextDecoder();
                let buffer = '';

                while (true) {
                    const { done, value } = await reader.read();
                    if (done) break;

                    buffer += decoder.decode(value, { stream: true });
                    const lines = buffer.split('\n');
                    buffer = lines.pop() ?? '';

                    let eventType = '';
                    for (const line of lines) {
                        if (line.startsWith('event:')) {
                            eventType = line.replace('event:', '').trim();
                        } else if (line.startsWith('data:')) {
                            const data = JSON.parse(line.replace('data:', '').trim());
                            if (eventType === 'routes_ready') {
                                console.log("Znaleziono trasy!", data);
                                setMatches(data.matches || []);
                                setIsLoading(false);
                                controller.abort();
                            } else if (eventType === 'no_match') {
                                console.log("Brak wyników");
                                setError("Niestety, nikt aktualnie nie jedzie w tym kierunku.");
                                setIsLoading(false);
                                controller.abort();
                            }
                            eventType = '';
                        }
                    }
                }
            } catch (err: any) {
                if (err.name !== 'AbortError') {
                    console.error("Błąd połączenia SSE:", err);
                    setIsLoading(false);
                }
            }
        };

        connectSSE();

        return () => {
            console.log("Zamknięto nasłuch SSE");
            controller.abort();
        };
    }, [jobId, token]);

    return { searchTrips, isLoading, matches, error };
};