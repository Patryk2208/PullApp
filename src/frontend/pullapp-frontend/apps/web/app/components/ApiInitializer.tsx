'use client';

import { useLayoutEffect } from 'react';
import { registerTokenProvider, registerUnauthorizedHandler } from '@pullapp/api-client';
import { useAuthStore } from '@pullapp/features';

export function ApiInitializer() {
	useLayoutEffect(() => {
		// Wstrzykujemy token z Zustand do Axiosa
		registerTokenProvider(() => {
			console.log('useAuthStore retrieves', useAuthStore.getState());
			return useAuthStore.getState().token;
		});
		
		// Wstrzykujemy akcję wylogowania dla błędu 401
		registerUnauthorizedHandler(() => {
			console.log("unauthorized handler")
			useAuthStore.getState().logout();
			if (typeof window !== 'undefined') {
				window.location.href = '/login';
			}
		});
	}, []);
	
	// Ten komponent nie generuje żadnego HTML.
	return null;
}