import * as Zustand from 'zustand';

interface AuthState {
	token: string | null;
	setToken: (token: string) => void;
	clearToken: () => void;
}

// TODO this is Infrastructure, isn't it?
export const useAuthStore = Zustand.create<AuthState>((set) => ({
	token: null,
	setToken: (token) => set({ token }),
	clearToken: () => set({ token: null }),
}));