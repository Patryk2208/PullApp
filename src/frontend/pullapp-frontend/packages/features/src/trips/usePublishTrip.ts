import { useState } from 'react';
import { useAuthStore } from '../auth/authStore';

export interface PublishTripQuery {
    start: { lat: number; lng: number };
    end: { lat: number; lng: number };
    capacity: number;
}

export interface PublishTripResult {
    routeId: string;
}

export const usePublishTrip = () => {
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [result, setResult] = useState<PublishTripResult | null>(null);
    const token = useAuthStore(state => state.token);

    const publishTrip = async (query: PublishTripQuery) => {
        setIsLoading(true);
        setError(null);
        setResult(null);

        try {
            const response = await fetch('/api/route/driver/routes', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...(token ? { 'Authorization': `Bearer ${token}` } : {})
                },
                body: JSON.stringify({
                    Start: { Latitude: query.start.lat, Longitude: query.start.lng },
                    End: { Latitude: query.end.lat, Longitude: query.end.lng },
                    Capacity: query.capacity
                })
            });

            if (!response.ok) {
                const text = await response.text();
                try {
                    const err = JSON.parse(text);
                    throw new Error(err.Message || err.detail || `Błąd: ${response.status}`);
                } catch {
                    throw new Error(`Błąd: ${response.status}`);
                }
            }

            const data = await response.json();
            setResult({ routeId: data.routeId });
        } catch (err: any) {
            setError(err.message);
        } finally {
            setIsLoading(false);
        }
    };

    return { publishTrip, isLoading, error, result };
};