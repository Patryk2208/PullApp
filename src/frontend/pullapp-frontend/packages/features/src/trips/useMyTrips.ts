import { useState, useEffect, useCallback } from 'react';
import { useAuthStore } from '../auth/authStore';
import { useRidesStore, type RideStatus } from './ridesStore';
import { useNotificationStream } from '../notifications/useNotificationStream';

// Prośba pasażera (RideRequest) ze źródła prawdy (GET /passenger/requests).
export interface MyRequest {
	requestId: string;
	routeId: string;
	status: string; // Pending | Accepted | Rejected
	createdAt: string;
}

// backend RideStatus (WaitingForActivation/WaitingForDriver/Started + endedAt) → status store'a
function mapRideStatus(r: any): RideStatus {
	if (r.endedAt) return 'ended';
	if (r.status === 'Started') return 'started';
	return 'accepted';
}

/**
 * Pobiera przejazdy i prośby pasażera z backendu (read-model GET) na wejściu,
 * hydruje store rides (żeby przeżyły refresh ze źródła prawdy) i zwraca prośby.
 * Odświeża się gdy SSE zmieni stan (accept/reject/end).
 */
export function useMyTrips() {
	const token = useAuthStore((s) => s.token);
	const hydrate = useRidesStore((s) => s.hydrate);
	const [requests, setRequests] = useState<MyRequest[]>([]);
	const [loading, setLoading] = useState(true);
	const [error, setError] = useState<string | null>(null);

	const fetchAll = useCallback(async () => {
		if (!token) { setLoading(false); return; }
		setError(null);
		try {
			const [ridesRes, reqRes] = await Promise.all([
				fetch('/api/route/passenger/rides',    { headers: { Authorization: `Bearer ${token}` } }),
				fetch('/api/route/passenger/requests', { headers: { Authorization: `Bearer ${token}` } }),
			]);
			if (ridesRes.ok) {
				const rides = await ridesRes.json();
				hydrate(rides.map((r: any) => ({
					rideId: r.rideId,
					routeId: r.routeId,
					driverId: r.driverId,
					chatRoomId: r.chatRoomId ?? undefined,
					status: mapRideStatus(r),
					updatedAt: Date.now(),
				})));
			}
			if (reqRes.ok) {
				const reqs = await reqRes.json();
				setRequests(reqs.map((r: any) => ({
					requestId: r.requestId, routeId: r.routeId, status: r.status, createdAt: r.createdAt,
				})));
			}
		} catch (e: any) {
			setError(e?.message ?? 'Nie udało się pobrać danych');
		} finally {
			setLoading(false);
		}
	}, [token, hydrate]);

	useEffect(() => { fetchAll(); }, [fetchAll]);

	// stan po stronie serwera zmienia się przy tych zdarzeniach → odśwież
	useNotificationStream(useCallback((e) => {
		if (e.type === 'ride_accepted' || e.type === 'ride_rejected' || e.type === 'ride_ended') fetchAll();
	}, [fetchAll]));

	return { requests, loading, error, refetch: fetchAll };
}
