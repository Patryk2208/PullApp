import * as Zustand from 'zustand';
import * as ZustandMiddleware from 'zustand/middleware';

interface AuthState {
	token: string | null;
	setToken: (token: string | null) => void;
	logout: () => void;
}

// TODO ARCHITECTURAL: This supports web only and should be move to the Infrastructure layer in the first place.
const universalStorage: ZustandMiddleware.StateStorage = {
	getItem: async (name: string): Promise<string | null> => {
		if (typeof window !== 'undefined') {
			// Jesteśmy w przeglądarce.
			return window.localStorage.getItem(name);
		}
		// Jesteśmy na serwerze (Next.js SSR) lub w React Native (wymaga innej obsługi)
		// W pełnej wersji dla Expo użyłbyś tu importu SecureStore z Expo, 
		// ale rozwiązanego przez mechanizm wstrzykiwania zależności lub osobny plik .native.ts
		return null;
	},
	setItem: async (name: string, value: string): Promise<void> => {
		if (typeof window !== 'undefined') {
			window.localStorage.setItem(name, value);
		}
	},
	removeItem: async (name: string): Promise<void> => {
		if (typeof window !== 'undefined') {
			window.localStorage.removeItem(name);
		}
	},
};

export const useAuthStore = Zustand.create<AuthState>()(
	ZustandMiddleware.persist(
		(set) => ({
			token: null,
			setToken: (token) => set({ token }),
			logout: () => set({ token: null }),
		}),
		{
			name: 'pullapp-auth-storage',
			storage: ZustandMiddleware.createJSONStorage(() => universalStorage),
		}
	)
);