import { useState } from 'react';
import { RideMatchingQuery, TripMatch } from '@pullapp/domain';
import { useAuthStore } from '../auth/authStore';

export const useSearchTrips = () => {
    const [isLoading, setIsLoading] = useState(false);
    const [matches, setMatches] = useState<TripMatch[]>([]);
    const [error, setError] = useState<string | null>(null);
    const token = useAuthStore(state => state.token);

    const searchTrips = async (query: RideMatchingQuery) => {
        setIsLoading(true);
        setError(null);
        setMatches([]);

        const controller = new AbortController();
        const timeoutId = setTimeout(() => {
            setError('Nie znaleziono pasujących przejazdów w wyznaczonym czasie.');
            setIsLoading(false);
            controller.abort();
        }, 60000);

        // 1. SSE w tle — startujemy nasłuch, ale NIE czekamy na niego przed POST-em.
        //    Gdyby SSE był zablokowany/wisiał (np. Firefox content-blocking), await
        //    tutaj uniemożliwiłby wysłanie wyszukiwania. .catch() pochłania odrzucenie
        //    na wypadek, gdyby POST padł i nigdy nie skonsumowali tego promise'a.
        const ssePromise = fetch('/api/sse', {
            headers: { Authorization: `Bearer ${token}`, Accept: 'text/event-stream' },
            signal: controller.signal,
        });
        ssePromise.catch(() => { /* obsłużone przy await poniżej */ });

        try {
            // 2. POST leci od razu, niezależnie od stanu SSE.
            const searchResponse = await fetch('/api/route/passenger/routes/search', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...(token ? { Authorization: `Bearer ${token}` } : {}),
                },
                body: JSON.stringify({
                    Start: { Latitude: query.start.lat, Longitude: query.start.lng },
                    End: { Latitude: query.end.lat, Longitude: query.end.lng },
                    DepartureDate: new Date(query.departureDate).getTime(),
                    SeatsNeeded: query.seatsNeeded,
                    MaxDetourKm: query.maxDetourKm,
                    TimeWindowMinutes: query.timeWindowMinutes,
                }),
            });

            // 3. Error-check PO POST-cie.
            if (!searchResponse.ok) {
                if (searchResponse.status === 422) throw new Error('Punkt startu lub celu jest poza obszarem działania serwisu.');
                if (searchResponse.status === 401) throw new Error('Sesja wygasła — zaloguj się ponownie.');
                throw new Error(`Wyszukiwanie odrzucone (${searchResponse.status}).`);
            }

            // 4. Dopiero teraz czekamy na SSE i czytamy wynik.
            const sseResponse = await ssePromise;
            if (!sseResponse.ok || !sseResponse.body) {
                throw new Error('Nie można połączyć się ze strumieniem wyników.');
            }

            const reader = sseResponse.body.getReader();
            const decoder = new TextDecoder();
            let buffer = '';
            let eventType = '';

            while (true) {
                const { done, value } = await reader.read();
                if (done) break;

                buffer += decoder.decode(value, { stream: true });
                const lines = buffer.split('\n');
                buffer = lines.pop() ?? '';

                for (const line of lines) {
                    if (line.startsWith('event:')) {
                        eventType = line.slice(6).trim();
                    } else if (line.startsWith('data:')) {
                        try {
                            const data = JSON.parse(line.slice(5).trim());
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
                                    setError('Niestety, nikt aktualnie nie jedzie w tym kierunku.');
                                }
                                setIsLoading(false);
                                controller.abort();
                                return;
                            }
                        } catch { /* niekompletny/nie-JSON data: — pomiń */ }
                        eventType = '';
                    }
                }
            }
        } catch (err: any) {
            clearTimeout(timeoutId);
            if (err?.name !== 'AbortError') {
                setError(err.message);
                setIsLoading(false);
            }
            controller.abort();
        }
    };

    return { searchTrips, isLoading, matches, error };
};
