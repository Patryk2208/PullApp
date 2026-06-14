import * as Zustand from 'zustand';
import * as ZustandMiddleware from 'zustand/middleware';

// UWAGA: backend trip-planner NIE ma endpointu GET dla rides pasażera — stan
// przejazdów istnieje wyłącznie w eventach SSE. Trzymamy go client-side i
// persystujemy do localStorage, żeby przetrwał reload (read-model po stronie
// backendu = osobny dług, zaznaczony w DECISIONS).

export type RideStatus = 'accepted' | 'started' | 'ended' | 'cancelled';

export interface PassengerRide {
	rideId: string;
	routeId: string;
	driverId?: string;
	chatRoomId?: string;
	status: RideStatus;
	updatedAt: number;
}

interface RidesState {
	rides: Record<string, PassengerRide>;
	/** zastosuj zdarzenie SSE do stanu rides */
	applyEvent: (type: string, data: any) => void;
	/** lokalna zmiana statusu (optymistyczna, po akcji pickup/end/cancel) */
	setStatus: (rideId: string, status: RideStatus) => void;
	/** zasil/zmerguj ze źródła prawdy (GET /passenger/rides) */
	hydrate: (items: PassengerRide[]) => void;
	clear: () => void;
}

const storage: ZustandMiddleware.StateStorage = {
	getItem: (n) => (typeof window !== 'undefined' ? window.localStorage.getItem(n) : null),
	setItem: (n, v) => { if (typeof window !== 'undefined') window.localStorage.setItem(n, v); },
	removeItem: (n) => { if (typeof window !== 'undefined') window.localStorage.removeItem(n); },
};

export const useRidesStore = Zustand.create<RidesState>()(
	ZustandMiddleware.persist(
		(set) => ({
			rides: {},
			applyEvent: (type, data) => set((s) => {
				const rides = { ...s.rides };
				const id = data?.RideId;
				if (type === 'ride_accepted' && id) {
					rides[id] = {
						rideId: id,
						routeId: data.RouteId,
						driverId: data.DriverId,
						chatRoomId: data.ChatRoomId,
						status: 'accepted',
						updatedAt: Date.now(),
					};
				} else if (type === 'ride_ended' && id && rides[id]) {
					rides[id] = { ...rides[id], status: 'ended', updatedAt: Date.now() };
				}
				return { rides };
			}),
			setStatus: (rideId, status) => set((s) => (
				s.rides[rideId]
					? { rides: { ...s.rides, [rideId]: { ...s.rides[rideId], status, updatedAt: Date.now() } } }
					: s
			)),
			hydrate: (items) => set((s) => {
				const rides = { ...s.rides };
				for (const it of items) rides[it.rideId] = { ...rides[it.rideId], ...it };
				return { rides };
			}),
			clear: () => set({ rides: {} }),
		}),
		{ name: 'pullapp-rides-storage', storage: ZustandMiddleware.createJSONStorage(() => storage) }
	)
);
