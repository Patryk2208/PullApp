import { useState } from 'react';
import { useAuthStore } from '../auth/authStore';
import { useRidesStore, type RideStatus } from './ridesStore';

// Akcje cyklu życia przejazdu (flow 7/8) — pasażer.
// Kontrakty zweryfikowane przeciw backendowi:
//   pickup: 204 (tylko PO deklaracji kierowcy, inaczej 403 declaration_order)
//   end:    204 (tylko w statusie Started, inaczej 409)
//   cancel: 204 (DELETE)
// UWAGA: backend NIE emituje eventu gdy kierowca zadeklaruje odbiór — pasażer
// nie wie kiedy może deklarować swój. Stąd graceful obsługa 403 (poczekaj).
export function useRideActions() {
	const token = useAuthStore((s) => s.token);
	const setStatus = useRidesStore((s) => s.setStatus);
	const [busy, setBusy] = useState<string | null>(null);
	const [error, setError] = useState<string | null>(null);

	async function call(rideId: string, method: string, path: string, nextStatus: RideStatus) {
		setBusy(rideId);
		setError(null);
		try {
			const res = await fetch(path, {
				method,
				headers: token ? { Authorization: `Bearer ${token}` } : undefined,
			});
			if (!res.ok) {
				let msg = `Błąd: ${res.status}`;
				try {
					const j = await res.json();
					if (j?.Code === 'declaration_order') msg = 'Kierowca jeszcze nie potwierdził odbioru — poczekaj chwilę i spróbuj ponownie.';
					else msg = j?.Message || msg;
				} catch { /* brak ciała */ }
				throw new Error(msg);
			}
			setStatus(rideId, nextStatus);
			return true;
		} catch (e: any) {
			setError(e.message);
			return false;
		} finally {
			setBusy(null);
		}
	}

	return {
		busy,
		error,
		pickup: (rideId: string) => call(rideId, 'POST', `/api/route/passenger/rides/${rideId}/pickup`, 'started'),
		end:    (rideId: string) => call(rideId, 'POST', `/api/route/passenger/rides/${rideId}/end`, 'ended'),
		cancel: (rideId: string) => call(rideId, 'DELETE', `/api/route/passenger/rides/${rideId}`, 'cancelled'),
	};
}
