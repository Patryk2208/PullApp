'use client';

import { useLayoutEffect } from 'react';
import { registerTokenProvider, registerUnauthorizedHandler } from '@pullapp/api-client';
import { useAuthStore } from '@pullapp/features';

export function ApiInitializer() {
	useLayoutEffect(() => {
		// token z Zustand → interceptor Axiosa
		registerTokenProvider(() => useAuthStore.getState().token);

		// 401 z API → wyloguj i odeślij na /login (jeśli już tam nie jesteśmy)
		registerUnauthorizedHandler(() => {
			useAuthStore.getState().logout();
			if (typeof window !== 'undefined' && window.location.pathname !== '/login') {
				window.location.href = '/login';
			}
		});
	}, []);
	
	// Ten komponent nie generuje żadnego HTML.
	return null;
}