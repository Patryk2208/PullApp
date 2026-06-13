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

        const controller = new AbortController();
        const timeoutId = setTimeout(() => {
            setError("Nie znaleziono pasujących przejazdów w wyznaczonym czasie.");
            setIsLoading(false);
            controller.abort();
        }, 60000);

        try {
            // 1. Otwórz SSE
            const sseResponse = await fetch('/api/sse', {
                headers: {
                    'Authorization': `Bearer ${token}`,
                    'Accept': 'text/event-stream',
                },
                signal: controller.signal,
            });

            if (!sseResponse.ok || !sseResponse.body) {
                throw new Error('Nie można połączyć się z serwerem powiadomień');
            }

            // 2. SSE gotowe (nagłówki odebrane) — wyślij POST
            const payload = {
                Start: { Latitude: query.start.lat, Longitude: query.start.lng },
                End: { Latitude: query.end.lat, Longitude: query.end.lng },
                DepartureDate: new Date(query.departureDate).getTime(),
                SeatsNeeded: query.seatsNeeded,
                MaxDetourKm: query.maxDetourKm,
                TimeWindowMinutes: query.timeWindowMinutes
            };

            const searchResponse = await fetch('/api/route/passenger/routes/search', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...(token ? { 'Authorization': `Bearer ${token}` } : {})
                },
                body: JSON.stringify(payload)
            });

            if (!searchResponse.ok) {
                throw new Error(`API odrzuciło żądanie: ${searchResponse.status}`);
            }

            // 3. Czytaj SSE aż do wyniku
            const reader = sseResponse.body.getReader();
            const decoder = new TextDecoder();
            let buffer = '';

            while (true) {
                const { done, value } = await reader.read();
                if (done) break;

                const chunk = decoder.decode(value, { stream: true });
                buffer += chunk;
                const lines = buffer.split('\n');
                buffer = lines.pop() ?? '';

                let eventType = '';
                for (const line of lines) {
                    if (line.startsWith('event:')) {
                        eventType = line.replace('event:', '').trim();
                    } else if (line.startsWith('data:')) {
                        try {
                            const data = JSON.parse(line.replace('data:', '').trim());
                            if (eventType === 'route_search_completed') {
                                clearTimeout(timeoutId);
                                if (data.Matches && data.Matches.length > 0) {
                                    setMatches(data.Matches.map((m: any) => ({
                                        routeId: m.RouteId,
                                        driverId: m.DriverId,
                                        matchScore: m.MatchScore,
                                        detourKm: m.DetourKm,
                                        pickupPointIndex: m.PickupPointIndex,
                                        dropoffPointIndex: m.DropoffPointIndex,
                                    })));
                                } else {
                                    setError("Niestety, nikt aktualnie nie jedzie w tym kierunku.");
                                }
                                setIsLoading(false);
                                controller.abort();
                                return;
                            }
                        } catch {}
                        eventType = '';
                    }
                }
            }

        } catch (err: any) {
            clearTimeout(timeoutId);
            if (err.name !== 'AbortError') {
                setError(err.message);
                setIsLoading(false);
            }
        }
    };

    return { searchTrips, isLoading, matches, error };
};