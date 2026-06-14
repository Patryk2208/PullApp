import { useEffect, useRef } from 'react';
import { useAuthStore } from '../auth/authStore';

export interface SseEvent {
	type: string;
	data: any;
}

/**
 * Trwały strumień SSE powiadomień. Łączy się gdy jest token, parsuje ramki
 * `event:`/`data:` i woła `onEvent` dla każdego zdarzenia. Reużywalny przez
 * wszystkie role (pasażer, kierowca) — jedno źródło prawdy dla notyfikacji.
 */
export function useNotificationStream(onEvent: (e: SseEvent) => void) {
	const token = useAuthStore((s) => s.token);
	// trzymamy najnowszy callback w ref, żeby nie restartować połączenia przy
	// każdej zmianie tożsamości funkcji — strumień zależy tylko od tokena
	const onEventRef = useRef(onEvent);
	onEventRef.current = onEvent;

	useEffect(() => {
		if (!token) return;
		const controller = new AbortController();

		(async () => {
			try {
				const res = await fetch('/api/sse', {
					headers: {
						Authorization: `Bearer ${token}`,
						Accept: 'text/event-stream',
					},
					signal: controller.signal,
				});
				if (!res.ok || !res.body) return;

				const reader = res.body.getReader();
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
							const raw = line.slice(5).trim();
							let parsed: any = raw;
							try { parsed = JSON.parse(raw); } catch { /* zostaw raw */ }
							onEventRef.current({ type: eventType || 'message', data: parsed });
							eventType = '';
						}
					}
				}
			} catch (err: any) {
				// AbortError = sprzątanie przy unmount/zmianie tokena — ignoruj
				if (err?.name !== 'AbortError') {
					console.warn('notification stream error', err);
				}
			}
		})();

		return () => controller.abort();
	}, [token]);
}
